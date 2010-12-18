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

        public static int GetDirectoryId(SQLiteConnection db, DriveInformation drive, string directoryPath) {

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

    }

}
