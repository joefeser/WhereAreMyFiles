/*
MIT License

Copyright (c) 2010 Joe Feser joseph.feser (at) gmail dot com

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Management;
using SQLite;
using System.Runtime.InteropServices;

namespace FileParser.Data {
    
    public class FileDataStore {
        
        [DllImport("kernel32.dll")]
        private static extern long GetVolumeInformation(string PathName, StringBuilder VolumeNameBuffer, UInt32 VolumeNameSize,
            ref UInt32 VolumeSerialNumber, ref UInt32 MaximumComponentLength, ref UInt32 FileSystemFlags,
            StringBuilder FileSystemNameBuffer, UInt32 FileSystemNameSize);

        public static void ProcessPath(string databasePath, string filePath) {

            using (var db = new SQLiteConnection(databasePath)) {

                db.BeginTransaction();

                db.CreateTable<DriveInformation>();
                //db.CreateTable<DrivePropertyInformation>();
                db.CreateTable<DirectoryInformation>();
                db.CreateTable<FileInformation>();
                db.CreateTable<FileAttributeInformation>();
                db.CreateTable<FileAttribute>();

                db.Commit();

                db.BeginTransaction();

                var queryDrives = DriveInfo.GetDrives();

                //var drives = new string[] { "C", "D", "J", "K", "S", "V", "Z" };

                //foreach (var drive in queryDrives) {
                //    //var serial = GetVolumeSerial(drive.Name);
                //    var vsn = GetHDDVolumnSerialNumber(drive.Name);
                //}
                var cmd = db.CreateCommand("Select * from DriveInformation");

                var hdCollection = cmd.ExecuteQuery<DriveInformation>(); //= new List<DriveInformation>();

                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

                foreach (ManagementObject wmi_HD in searcher.Get()) {

                    var query = "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + wmi_HD["DeviceID"] + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
                    //Set wmiDiskPartitions = wmiServices.ExecQuery(query)

                    var searcher2 = new ManagementObjectSearcher(query);

                    foreach (ManagementObject association in searcher2.Get()) {

                        query = "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + association["DeviceID"] + "'} WHERE AssocClass = Win32_LogicalDiskToPartition";
                        var searcher3 = new ManagementObjectSearcher(query);
                        foreach (ManagementObject driveLetter in searcher3.Get()) {
                            //we want one added for every drive letter on the disk.
                            var dl = driveLetter["DeviceID"].ToString().Substring(0, 1);
                            var hd = hdCollection.FirstOrDefault(drive => drive.DriveLetter == dl);
                            if (hd == null) {
                                hd = new DriveInformation();
                                hd.DriveLetter = dl;
                                hd.Model = wmi_HD["Model"].ToString();
                                hd.DriveType = wmi_HD["InterfaceType"].ToString();
                                hdCollection.Add(hd);
                                db.Insert(hd);
                            }
                            else {
                                hd.Model = wmi_HD["Model"].ToString();
                                hd.DriveType = wmi_HD["InterfaceType"].ToString();
                                db.Update(hd);
                            }
                        }
                    }
                }

                searcher = new ManagementObjectSearcher("Select * from Win32_LogicalDisk");
                var moc = searcher.Get();

                foreach (ManagementObject mo in moc) {
                    string driveLetter = mo["DeviceId"].ToString().Substring(0, 1);
                    var vn = (mo["VolumeName"] ?? string.Empty).ToString().Trim();
                    var vsn = (mo["VolumeSerialNumber"] ?? string.Empty).ToString().Trim();
                    var driveType = mo["DriveType"].ToString();

                    var hd = hdCollection.FirstOrDefault(item => item.DriveLetter == driveLetter);
                    if (hd == null) {
                        hd = new DriveInformation() {
                            DriveLetter = driveLetter
                        };
                        hdCollection.Add(hd);
                        hd.DriveType = driveType;
                        hd.SerialNo = vsn;
                        hd.VolumeName = vn;
                        db.Insert(hd);
                    }
                    else {
                        hd.DriveType = driveType;
                        hd.SerialNo = vsn;
                        hd.VolumeName = vn;
                        db.Update(hd);
                    }
                }

                db.Commit();

                var start = DateTime.Now;

                List<string> arrHeaders = GetFileAttributeList(db, @"d:\");

                ProcessFolder(db, hdCollection, arrHeaders, filePath);

                //just in case something blew up and it is not committed.
                if (db.IsInTransaction) {
                    db.Commit();
                }

                //db.Commit();

                db.Close();
            }
        }

        private static void ProcessFolder(SQLiteConnection db, List<DriveInformation> hdCollection, List<string> arrHeaders, string processPath) {

            try {

                var di = new DirectoryInfo(processPath);

                if (!di.Exists)
                    return;

                //TODO line it up with the size or the serial number since we will have removable drives.
                var drive = hdCollection.FirstOrDefault(letter => letter.DriveLetter.Equals(processPath.Substring(0, 1), StringComparison.OrdinalIgnoreCase));

                var sections = di.FullName.Split('\\');
                var directoryPath = string.Join(@"\", sections.Skip(1));

                //go get the cached items for the folder.

                var directoryId = GetDirectoryId(db, drive, directoryPath);

                var cmd = db.CreateCommand("Select * from " + typeof(FileInformation).Name + " Where DriveId = ? AND DirectoryId = ?", drive.DriveId, directoryId);
                var folderItems = cmd.ExecuteQuery<FileInformation>();

                db.BeginTransaction();

                Shell32.Shell shell = new Shell32.Shell();
                Shell32.Folder folder = shell.NameSpace(processPath);

                foreach (Shell32.FolderItem2 item in folder.Items()) {
                    try {

                        string type = string.Empty;
                        string path = string.Empty;

                        var headerList = new List<FileAttributeInformation>();

                        for (int i = 0; i < arrHeaders.Count; i++) {
                            var header = arrHeaders[i];
                            var value = folder.GetDetailsOf(item, i);

                            if (!string.IsNullOrWhiteSpace(value)) {

                                if (header.Equals("Attributes", StringComparison.OrdinalIgnoreCase)) {
                                    type = value;
                                }
                                if (header.Equals("Path", StringComparison.OrdinalIgnoreCase)) {
                                    path = value;
                                }

                                headerList.Add(new FileAttributeInformation() {
                                    AttributeId = GetAttributeId(db, header),
                                    Value = value
                                });
                                //sw.WriteLine("{0}\t{1}: {2}", i, header, value);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(path)) {
                            var fi = new FileInfo(path);
                            if (fi.Exists) {
                                FileInformation fileInfo = folderItems.FirstOrDefault(info => info.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                                bool isNew = false;

                                if (fileInfo == null) {
                                    fileInfo = new FileInformation() {
                                        CreatedDate = fi.CreationTimeUtc,
                                        DriveId = drive.DriveId,
                                        DirectoryId = directoryId,
                                        FileName = fi.Name,
                                        Hash = fi.Length < 2000000000 ? ComputeShaHash(path) : "NONE",
                                        LastWriteDate = fi.LastWriteTimeUtc,
                                        Length = fi.Length
                                    };

                                    db.Insert(fileInfo);
                                    headerList.ForEach(hl => hl.FileId = fileInfo.FileId);
                                    db.InsertAll(headerList);
                                    isNew = true;
                                    Console.WriteLine("Inserted:" + path);
                                }

                                if (!isNew) {
                                    if (fi.Length != fileInfo.Length
                                        || fi.CreationTimeUtc.NoMilliseconds() != fileInfo.CreatedDate
                                        || fi.LastWriteTimeUtc.NoMilliseconds() != fileInfo.LastWriteDate) {

                                        fileInfo.CreatedDate = fi.CreationTimeUtc;
                                        fileInfo.Hash = fi.Length < 2000000000 ? ComputeShaHash(path) : "NONE";
                                        fileInfo.LastWriteDate = fi.LastWriteTimeUtc;
                                        fileInfo.Length = fi.Length;
                                        db.Update(fileInfo);

                                        var deleteCount = db.Execute("Delete from " + typeof(FileAttributeInformation).Name + " WHERE FileId = ?", fileInfo.FileId);

                                        headerList.ForEach(hl => hl.FileId = fileInfo.FileId);
                                        db.InsertAll(headerList);

                                        Console.WriteLine("Changed:" + path);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) {
                        Console.WriteLine(ex.ToString());
                        //throw;
                    }
                }

                db.Commit();

                //see if we have any additional folders. If we get access denied it will throw an error
                try {
                    foreach (var directory in di.GetDirectories()) {
                        ProcessFolder(db, hdCollection, arrHeaders, directory.FullName);
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }

        }

        private static int GetDirectoryId(SQLiteConnection db, DriveInformation drive, string directoryPath) {

            directoryPath = (directoryPath ?? string.Empty).Trim();

            var cmd = db.CreateCommand("Select * from " + typeof(DirectoryInformation).Name + " Where DriveId = ? AND Path = ?", drive.DriveId, directoryPath);
            var retVal = cmd.ExecuteQuery<DirectoryInformation>().FirstOrDefault();

            if (retVal != null) {
                return retVal.DirectoryId;
            }

            var actualDirectoryInfo = new DirectoryInfo(Path.Combine(drive.DriveLetter + @":\", directoryPath));
            var name = actualDirectoryInfo.FullName.Substring(3);

            int? parentDirectoryInfo = null;

            if (actualDirectoryInfo.Parent != null) {
                var parentName = actualDirectoryInfo.Parent.FullName.Substring(3);
                parentDirectoryInfo = GetDirectoryId(db, drive, parentName);
            }

            //create a new record
            var directory = new DirectoryInformation() {
                DriveId = drive.DriveId,
                Name = name,
                ParentDirectoryId = parentDirectoryInfo.HasValue ? parentDirectoryInfo : null,
                Path = directoryPath
            };

            db.Insert(directory);

            return directory.DirectoryId;
        }

        private static List<string> GetFileAttributeList(SQLiteConnection db, string path) {
            var arrHeaders = new List<string>();

            Shell32.Shell shell = new Shell32.Shell();
            Shell32.Folder folder = shell.NameSpace(@"C:\");

            for (int i = 0; i < short.MaxValue; i++) {
                string header = folder.GetDetailsOf(null, i);
                if (String.IsNullOrEmpty(header))
                    break;
                arrHeaders.Add(header);
                //add the header to the db.
                var attId = GetAttributeId(db, header);
            }
            return arrHeaders;
        }

        private static List<FileAttribute> _fileAttributes = null;

        private static int GetAttributeId(SQLiteConnection db, string name) {

            if (_fileAttributes == null) {
                _fileAttributes = new List<FileAttribute>();
                _fileAttributes.AddRange(db.Table<FileAttribute>());
            }

            var fi = _fileAttributes.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (fi == null) {
                fi = new FileAttribute() {
                    Name = name
                };
                db.Insert(fi);
                _fileAttributes.Add(fi);
            }
            return fi.AttributeId;

        }

        /// <summary>
        /// method to retrieve the selected HDD's serial number
        /// </summary>
        /// <param name="strDriveLetter">Drive letter to retrieve serial number for</param>
        /// <returns>the HDD's serial number</returns>
        public static string GetHDDVolumnSerialNumber(string drive) {
            //check to see if the user provided a drive letter
            //if not default it to "C"
            if (drive == "" || drive == null) {
                drive = "C";
            }

            if (drive.EndsWith("\\")) {
                drive = drive.Substring(0, drive.Length - 1);
            }

            if (!drive.EndsWith(":")) {
                drive = drive + ":";
            }

            //create our ManagementObject, passing it the drive letter to the
            //DevideID using WQL
            ManagementObject disk = new ManagementObject("win32_logicaldisk.deviceid=\"" + drive + "\"");
            //bind our management object
            disk.Get();

            var items = new List<PropertyData>(disk.Properties.Cast<PropertyData>());

            //var di = new DriveInfo(drive);

            //return the serial number
            return (disk["VolumeSerialNumber"] ?? string.Empty).ToString();
        }

        //public static string GetVolumeSerial(string strDriveLetter) {
        //    uint serNum = 0;
        //    uint maxCompLen = 0;
        //    StringBuilder VolLabel = new StringBuilder(256); // Label
        //    UInt32 VolFlags = new UInt32();
        //    StringBuilder FSName = new StringBuilder(256); // File System Name
        //    strDriveLetter += ":\\"; // fix up the passed-in drive letter for the API call
        //    long Ret = GetVolumeInformation(strDriveLetter, VolLabel, (UInt32)VolLabel.Capacity, ref serNum, ref maxCompLen, ref VolFlags, FSName, (UInt32)FSName.Capacity);

        //    return Convert.ToString(serNum);
        //}

        public static string ComputeShaHash(string path) {
            if (!File.Exists(path)) {
                return string.Empty;
            }
            using (var stream = File.OpenRead(path)) {
                return ComputeShaHash(stream);
            }
        }

        public static string ComputeShaHash(Stream stream) {
            var hasher = System.Security.Cryptography.SHA1Managed.Create();
            var hash = hasher.ComputeHash(stream);

            return ToHexString(hash);
        }

        public static string ToHexString(byte[] bytes) {

            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            return bytes.Aggregate(new StringBuilder(), (sb, b) => sb.AppendFormat("{0:X2}", b))
                        .ToString();
        }
    }
}
