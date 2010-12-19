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
using SQLite;
using System.IO;

namespace FileParser.Data {

    public static class DatabaseLookups {

        public static void CreateTables(SQLiteConnection db) {
            db.BeginTransaction();

            db.CreateTable<DriveInformation>();
            db.CreateTable<DirectoryInformation>();
            db.CreateTable<FileInformation>();
            db.CreateTable<FileAttributeInformation>();
            db.CreateTable<FileAttribute>();

            db.Execute("CREATE INDEX if not exists \"main\".\"ix_DirectoryInformation_driveid_path\" ON \"DirectoryInformation\" (\"DriveId\" ASC, \"Path\" ASC)");
            db.Execute("CREATE INDEX if not exists \"main\".\"ix_FileInformation_driveid_directoryid\" ON \"FileInformation\" (\"DirectoryId\" ASC, \"DriveId\" ASC)");

            db.Commit();
        }

        private static List<FileAttribute> _fileAttributes = null;

        public static int GetAttributeId(SQLiteConnection db, string name) {

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

        public static int GetDirectoryId(SQLiteConnection db, DriveInformation drive, DirectoryInfo directory) {

            var directoryPath = directory.ToDirectoryPath();
            directoryPath = (directoryPath ?? string.Empty).Trim();

            var cmd = db.CreateCommand("Select * from " + typeof(DirectoryInformation).Name + " Where DriveId = ? AND Path = ?", drive.DriveId, directoryPath);
            var retVal = cmd.ExecuteQuery<DirectoryInformation>().FirstOrDefault();

            if (retVal != null) {
                return retVal.DirectoryId;
            }

            int? parentDirectoryInfo = null;

            if (directory.Parent != null) {
                var parentName = directory.Parent.FullName.Substring(3);
                parentDirectoryInfo = GetDirectoryId(db, drive, directory.Parent);
            }

            var directoryName = directory.Name;

            if (directoryName.IndexOf(":") > -1) {
                directoryName = directoryName.Substring(3);
            }

            //create a new record
            var newDirectory = new DirectoryInformation() {
                DriveId = drive.DriveId,
                Name = directoryName,
                ParentDirectoryId = parentDirectoryInfo.HasValue ? parentDirectoryInfo : null,
                Path = directoryPath
            };

            db.Insert(newDirectory);

            return newDirectory.DirectoryId;
        }

    }

}
