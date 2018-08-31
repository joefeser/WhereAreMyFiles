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
using MongoDB.Driver;
using System.Threading.Tasks;

namespace FileParser.Data {

    public static class DatabaseLookups {

        private static IMongoClient client = new MongoClient();
        private static IMongoDatabase database;

        private static readonly string DriveInformation = typeof(DriveInformation).Name.ToLower();
        private static readonly string DirectoryInformation = typeof(DirectoryInformation).Name.ToLower();
        private static readonly string FileInformation = typeof(FileInformation).Name.ToLower();
        private static readonly string FileAttributeInformation = typeof(FileAttributeInformation).Name.ToLower();
        private static readonly string FileAttribute = typeof(FileAttribute).Name.ToLower();

        private static IMongoCollection<DriveInformation> DriveInformationCollection;
        private static IMongoCollection<DirectoryInformation> DirectoryInformationCollection;
        private static IMongoCollection<FileInformation> FileInformationCollection;
        private static IMongoCollection<FileAttributeInformation> FileAttributeInformationCollection;
        private static IMongoCollection<FileAttribute> FileAttributeCollection;

        public static async Task CreateTables() {

            database = client.GetDatabase("files");
            database.CreateCollection(DriveInformation);
            database.CreateCollection(DirectoryInformation);
            database.CreateCollection(FileInformation);
            database.CreateCollection(FileAttributeInformation);
            database.CreateCollection(FileAttribute);

            DriveInformationCollection = database.GetCollection<DriveInformation>(FileAttribute);
            DirectoryInformationCollection = database.GetCollection<DirectoryInformation>(FileAttribute);
            FileInformationCollection = database.GetCollection<FileInformation>(FileAttribute);
            FileAttributeInformationCollection = database.GetCollection<FileAttributeInformation>(FileAttribute);
            FileAttributeCollection = database.GetCollection<FileAttribute>(FileAttribute);

            await DirectoryInformationCollection.Indexes.CreateOneAsync(new CreateIndexModel<DirectoryInformation>(Builders<DirectoryInformation>
                .IndexKeys.Ascending((item) => item.DriveId)
                .Ascending((item) => item.Path)));

            await FileInformationCollection.Indexes.CreateOneAsync(new CreateIndexModel<FileInformation>(Builders<FileInformation>
                .IndexKeys.Ascending((item) => item.DirectoryId)
                .Ascending((item) => item.DriveId)));
        }

        private static List<FileAttribute> fileAttributes = new List<FileAttribute>();

        public async static Task<Guid> GetAttributeId(string name) {

            if (fileAttributes == null) {
                fileAttributes.AddRange((await FileAttributeCollection.FindAsync(Builders<FileAttribute>.Filter.Empty)).ToEnumerable());
            }

            var fi = fileAttributes.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (fi == null) {
                fi = new FileAttribute() {
                    Name = name
                };
                await FileAttributeCollection.ReplaceOneAsync(Builders<FileAttribute>.Filter.Eq(item => item.Name, fi.Name), fi);
                fileAttributes.Add(fi);
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
