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

        public static void ProcessPath(string databasePath, string filePath) {

            //TODO ensure directory exists for the database
            using (var db = new SQLiteConnection(databasePath)) {

                DatabaseLookups.CreateTables(db);

                var hdCollection = DriveUtilities.ProcessDriveList(db);

                var start = DateTime.Now;

                List<string> arrHeaders = DriveUtilities.GetFileAttributeList(db);

                var di = new DirectoryInfo(filePath);

                if (di.Exists) {
                    ProcessFolder(db, hdCollection, arrHeaders, di);
                }

                //just in case something blew up and it is not committed.
                if (db.IsInTransaction) {
                    db.Commit();
                }

                db.Close();
            }
        }

        private static void ProcessFolder(SQLiteConnection db, List<DriveInformation> hdCollection, List<string> arrHeaders, DirectoryInfo directory) {

            try {

                var di = directory;

                if (!di.Exists)
                    return;

                var driveLetter = di.FullName.Substring(0, 1);

                //TODO line it up with the size or the serial number since we will have removable drives.
                var drive = hdCollection.FirstOrDefault(letter => letter.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));

                var directoryPath = di.ToDirectoryPath();

                if (IgnoreFolder(directoryPath)) {
                    return;
                }

                //go get the cached items for the folder.

                var directoryId = DatabaseLookups.GetDirectoryId(db, drive, directoryPath);

                var cmd = db.CreateCommand("Select * from " + typeof(FileInformation).Name + " Where DriveId = ? AND DirectoryId = ?", drive.DriveId, directoryId);
                var databaseFiles = cmd.ExecuteQuery<FileInformation>();

                //obtain the file metadata for all of the files in the directory so we can determine if we care about this folder.

                var processList = GetFilesToProcess(databaseFiles, arrHeaders, di);

                if (processList.Count > 0) {

                    db.BeginTransaction();

                    Shell32.Shell shell = new Shell32.Shell();
                    Shell32.Folder folder = shell.NameSpace(directory.FullName);

                    foreach (var item in processList) {
                        try {
                            var fi = item.FileInfo;
                            var headerList = new List<FileAttributeInformation>();

                            for (int i = 0; i < arrHeaders.Count; i++) {

                                var header = arrHeaders[i];

                                if (!IgnoreHeader(header)) {
                                    var value = folder.GetDetailsOf(item.FolderItem, i);

                                    if (!string.IsNullOrWhiteSpace(value)) {
                                        headerList.Add(new FileAttributeInformation() {
                                            AttributeId = DatabaseLookups.GetAttributeId(db, header),
                                            Value = value
                                        });
                                    }
                                }
                            }

                            //this should have been already checked but we want to be safe.
                            if (fi.Exists) {

                                var fileInfo = databaseFiles.FirstOrDefault(info => info.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                                if (fileInfo == null) {
                                    fileInfo = new FileInformation() {
                                        DriveId = drive.DriveId,
                                        DirectoryId = directoryId,
                                        FileName = fi.Name
                                    };
                                    SetFileInformation(fi, fileInfo);
                                    db.Insert(fileInfo);
                                    Console.WriteLine("Inserted:" + fi.FullName);
                                }
                                else {
                                    SetFileInformation(fi, fileInfo);
                                    db.Update(fileInfo);

                                    var deleteCount = db.Execute("Delete from " + typeof(FileAttributeInformation).Name + " WHERE FileId = ?", fileInfo.FileId);
                                    Console.WriteLine("Changed:" + fi.FullName);
                                }

                                //save the headers
                                headerList.ForEach(hl => hl.FileId = fileInfo.FileId);
                                db.InsertAll(headerList);
                            }
                        }
                        catch (Exception ex) {
                            Console.WriteLine(ex.ToString());
                        }
                    }

                    db.Commit();

                }

                //see if we have any additional folders. If we get access denied it will throw an error
                try {
                    foreach (var subDirectory in di.GetDirectories()) {
                        ProcessFolder(db, hdCollection, arrHeaders, subDirectory);
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.ToString());
                }
            }
            catch (UnauthorizedAccessException) {

            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }

        }

        private static List<string> _directoryIgnoreList = new List<string>(new string[] { "$RECYCLE.BIN", "System Volume Information" });

        private static bool IgnoreFolder(string folder) {
            var ignore = _directoryIgnoreList.Any(il => il.Equals(folder, StringComparison.OrdinalIgnoreCase));
            return ignore;
        }

        private static List<string> _ignoreHeaderList = new List<string>(new string[] { "Name", "Size", "Date modified", 
                                    "Date created", "Date accessed", "Filename", 
                                    "Folder name", "Folder path", "Folder", "Path" });

        private static bool IgnoreHeader(string header) {
            var ignore = _ignoreHeaderList.Any(il => il.Equals(header, StringComparison.OrdinalIgnoreCase));
            return ignore;
        }

        private static void SetFileInformation(FileInfo fi, FileInformation fileInfo) {
            fileInfo.CreatedDate = fi.CreationTimeUtc;
            fileInfo.Hash = fi.Length < 2000000000 ? HelperMethods.ComputeShaHash(fi.FullName) : "NONE";
            fileInfo.LastWriteDate = fi.LastWriteTimeUtc;
            fileInfo.Length = fi.Length;
        }

        private static List<FileAndFolder> GetFilesToProcess(List<FileInformation> databaseFiles, List<string> arrHeaders, DirectoryInfo di) {

            var filesToProcess = new List<FileInfo>();

            if (di.Exists) {

                foreach (var fi in di.GetFiles()) {
                    var fileInfo = databaseFiles.FirstOrDefault(info => info.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));

                    if (fileInfo == null) {
                        filesToProcess.Add(fi);
                    }
                    else if (fi.Length != fileInfo.Length
                                        || fi.CreationTimeUtc.NoMilliseconds() != fileInfo.CreatedDate
                                        || fi.LastWriteTimeUtc.NoMilliseconds() != fileInfo.LastWriteDate) {
                        filesToProcess.Add(fi);
                    }
                }
            }

            var retVal = new List<FileAndFolder>();

            if (filesToProcess.Count > 0) {

                var headerName = "Name";
                var nameIndex = arrHeaders.IndexOf(headerName);

                //reduce the folderinfo2 into a list we can process.

                Shell32.Shell shell = new Shell32.Shell();
                Shell32.Folder folder = shell.NameSpace(di.FullName);

                //thanks to this post http://geraldgibson.net/dnn/Home/CZipFileCompression/tabid/148/Default.aspx
                var nonfiltered = (Shell32.FolderItems3)folder.Items();
                int SHCONTF_INCLUDEHIDDEN = 128;
                int SHCONTF_NONFOLDERS = 64;
                nonfiltered.Filter(SHCONTF_INCLUDEHIDDEN | SHCONTF_NONFOLDERS, "*");

                foreach (Shell32.FolderItem2 item in nonfiltered) {
                    var value = folder.GetDetailsOf(item, nameIndex);
                    //see if we should process this item.
                    var processItem = filesToProcess.FirstOrDefault(fa => fa.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (processItem != null) {
                        retVal.Add(new FileAndFolder() {
                            FileInfo = processItem,
                            FolderItem = item
                        });
                    }
                }
            }
            return retVal;
        }

        private class FileAndFolder {
            public FileInfo FileInfo {
                get;
                set;
            }

            public Shell32.FolderItem2 FolderItem {
                get;
                set;
            }
        }

    }
}
