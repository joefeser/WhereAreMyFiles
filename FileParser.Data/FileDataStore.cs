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

                List<string> arrHeaders = GetFileAttributeList(db);

                ProcessFolder(db, hdCollection, arrHeaders, filePath);

                //just in case something blew up and it is not committed.
                if (db.IsInTransaction) {
                    db.Commit();
                }

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

                var directoryId = DatabaseLookups.GetDirectoryId(db, drive, directoryPath);

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
                                    AttributeId = DatabaseLookups.GetAttributeId(db, header),
                                    Value = value
                                });
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
                                        Hash = fi.Length < 2000000000 ? HelperMethods.ComputeShaHash(path) : "NONE",
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
                                        fileInfo.Hash = fi.Length < 2000000000 ? HelperMethods.ComputeShaHash(path) : "NONE";
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

        private static List<string> GetFileAttributeList(SQLiteConnection db) {
            //TODO determine if we need to use the drive we are on or can we use any folder. Also can this list change?
            var arrHeaders = new List<string>();

            Shell32.Shell shell = new Shell32.Shell();
            Shell32.Folder folder = shell.NameSpace(@"C:\");

            for (int i = 0; i < short.MaxValue; i++) {
                string header = folder.GetDetailsOf(null, i);
                if (String.IsNullOrEmpty(header))
                    break;
                arrHeaders.Add(header);
                //add the header to the db.
                var attId = DatabaseLookups.GetAttributeId(db, header);
            }
            return arrHeaders;
        }

    }
}
