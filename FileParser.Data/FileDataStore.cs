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
using System.IO;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace FileParser.Data {

    public class FileDataStore {

        public static async Task ProcessPath(string filePath) {

            await DatabaseLookups.CreateTables();

            var hdCollection = await DriveUtilities.ProcessDriveList();

            var start = DateTime.Now;

            List<string> arrHeaders = DriveUtilities.GetFileAttributeList();

            var directory = new DirectoryInfo(filePath);

            var driveLetter = directory.FullName.Substring(0, 1);
            //TODO line it up with the size or the serial number since we will have removable drives.
            var drive = hdCollection.FirstOrDefault(letter => letter.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase));

            if (directory.Exists) {
                await ProcessFolder(drive, directory, arrHeaders);
            }
            Console.WriteLine($"Completed {filePath} {DateTime.Now.ToLongTimeString()}");
        }

        private static async Task ProcessFolder(DriveInformation drive, DirectoryInfo directory, List<string> arrHeaders) {

            try {

                if (!directory.Exists)
                    return;

                if (IgnoreFolder(directory)) {
                    return;
                }

                //go get the cached items for the folder.

                var directoryId = await DatabaseLookups.GetDirectoryId(drive, directory);

                //var cmd = db.CreateCommand("Select * from " + typeof(FileInformation).Name + " Where DriveId = ? AND DirectoryId = ?", drive.Id, directoryId);
                var databaseFiles = await (await DatabaseLookups.FileInformationCollection.FindAsync(Builders<FileInformation>.Filter.Where(item => item.DriveId == drive.Id && item.DirectoryId == directoryId))).ToListAsync();

                //obtain the file metadata for all of the files in the directory so we can determine if we care about this folder.

                var processList = GetFilesToProcess(databaseFiles, arrHeaders, directory);

                if (processList.Count > 0) {

                    var folder = DriveUtilities.GetShell32Folder(directory.FullName);

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
                                            Attribute = header,
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
                                        DriveId = drive.Id,
                                        DirectoryId = directoryId,
                                        FileName = fi.Name
                                    };
                                    SetFileInformation(fi, fileInfo);
                                    await DatabaseLookups.FileInformationCollection.InsertOneAsync(fileInfo);
                                    Console.WriteLine("Inserted:" + fi.FullName);
                                }
                                else {
                                    SetFileInformation(fi, fileInfo);
                                    await DatabaseLookups.FileInformationCollection.ReplaceOneAsync(f => f.Id == fileInfo.Id, fileInfo);

                                    var deleteCount = await DatabaseLookups.FileAttributeInformationCollection.DeleteManyAsync(Builders<FileAttributeInformation>.Filter.Where(f => f.FileId == fileInfo.Id));
                                    Console.WriteLine("Changed:" + fi.FullName);
                                }

                                //save the headers
                                headerList.ForEach(hl => hl.FileId = fileInfo.Id);

                                await DatabaseLookups.FileAttributeInformationCollection.InsertManyAsync(headerList);
                            }
                        }
                        catch (Exception ex) {
                            Console.WriteLine(ex.ToString());
                        }
                    }
                }

                //see if we have any additional folders. If we get access denied it will throw an error
                try {
                    foreach (var subDirectory in directory.GetDirectories()) {
                        await ProcessFolder(drive, subDirectory, arrHeaders);
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

        private static List<string> _directoryIgnoreList = new List<string>(new string[] { "$RECYCLE.BIN", "System Volume Information", ".git" });

        private static bool IgnoreFolder(DirectoryInfo di) {
            var folder = di.ToDirectoryPath();
            var ignore = _directoryIgnoreList.Any(il => il.Equals(folder, StringComparison.OrdinalIgnoreCase));
            return ignore;
        }

        private static List<string> _ignoreHeaderList = new List<string>(new string[] { "Computer",
                                                                                        "Date accessed",
                                                                                        "Date created",
                                                                                        "Date modified",
                                                                                        "Filename",
                                                                                        "Folder name",
                                                                                        "Folder path",
                                                                                        "Folder",
                                                                                        "Name",
                                                                                        "Owner",
                                                                                        "Path",
                                                                                        "Size" });

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

            var databaseDictionary = databaseFiles.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
            var filesToProcess = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var allFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

            if (di.Exists) {
                foreach (var fi in di.GetFiles()) {
                    databaseDictionary.TryGetValue(fi.Name, out FileInformation fileInfo);
                    allFiles[fi.Name] = fi;
                    if (fileInfo == null) {
                        filesToProcess[fi.Name] = fi;
                    }
                    else if (fi.Length != fileInfo.Length
                                        || fi.CreationTimeUtc.NoMilliseconds() != fileInfo.CreatedDate.NoMilliseconds()
                                        || fi.LastWriteTimeUtc.NoMilliseconds() != fileInfo.LastWriteDate.NoMilliseconds()) {
                        filesToProcess[fi.Name] = fi;
                    }
                }
            }

            var retVal = new List<FileAndFolder>();

            if (filesToProcess.Count > 0) {
                var headerName = "Name";
                var nameIndex = arrHeaders.IndexOf(headerName);

                //reduce the folderinfo2 into a list we can process.

                var folder = DriveUtilities.GetShell32Folder(di.FullName);

                //thanks to this post http://geraldgibson.net/dnn/Home/CZipFileCompression/tabid/148/Default.aspx
                var nonfiltered = (Shell32.FolderItems3)folder.Items();
                int SHCONTF_INCLUDEHIDDEN = 128;
                int SHCONTF_NONFOLDERS = 64;
                nonfiltered.Filter(SHCONTF_INCLUDEHIDDEN | SHCONTF_NONFOLDERS, "*");

                foreach (Shell32.FolderItem2 item in nonfiltered) {
                    var value = folder.GetDetailsOf(item, nameIndex);
                    //see if we should process this item.
                    filesToProcess.TryGetValue(value, out FileInfo processItem);
                    if (processItem != null) {
                        retVal.Add(new FileAndFolder() {
                            FileInfo = processItem,
                            FolderItem = item
                        });
                    }
                    else {
                        allFiles.TryGetValue(value, out FileInfo pi);
                        if (pi == null && !IsShortcut(folder, value)) {
                            //see if this file exists somewhere.
                            Console.WriteLine($"Why are we not able to find the file? {value}");
                        }
                    }
                }
            }
            return retVal;
        }

        //https://code.msdn.microsoft.com/windowsdesktop/Identifying-and-Resolving-ca0dfce8

        private static bool IsShortcut(Shell32.Folder folder, string fileName) {
            object folderItem = new object();
            folderItem = folder.ParseName(fileName);

            if (folderItem != null) {
                return ((Shell32.FolderItem)folderItem).IsLink;
            }

            return false;
        }

        //private bool IsShortcut(string path) {
        //    string directory = Path.GetDirectoryName(path);
        //    string file = Path.GetFileName(path);

        //    Shell32.Shell shell = new Shell32.Shell();
        //    Shell32.Folder folder = shell.NameSpace(directory);
        //    Shell32.FolderItem folderItem = folder.ParseName(file);

        //    if (folderItem != null) {
        //        return folderItem.IsLink;
        //    }

        //    return false;
        //}

        //private string ResolveShortcut(string path) {
        //    string directory = Path.GetDirectoryName(path);
        //    string file = Path.GetFileName(path);

        //    Shell32.Shell shell = new Shell32.Shell();
        //    Shell32.Folder folder = shell.NameSpace(directory);
        //    Shell32.FolderItem folderItem = folder.ParseName(file);

        //    Shell32.ShellLinkObject link = (Shell32.ShellLinkObject)folderItem.GetLink;

        //    return link.Path;
        //}

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
