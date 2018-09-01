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

        public static IMongoCollection<DriveInformation> DriveInformationCollection;
        public static IMongoCollection<DirectoryInformation> DirectoryInformationCollection;
        public static IMongoCollection<FileInformation> FileInformationCollection;
        public static IMongoCollection<FileAttributeInformation> FileAttributeInformationCollection;

        static DatabaseLookups() {
            database = client.GetDatabase("files");

            var actions = new List<Action>() {
                () => database.CreateCollection(DriveInformation),
                () => database.CreateCollection(DirectoryInformation),
                () => database.CreateCollection(FileInformation),
                () => database.CreateCollection(FileAttributeInformation)
            };

            foreach (var action in actions) {
                try {
                    action();
                }
                catch (Exception ex) {
                    Console.WriteLine(ex.Message);
                }
            }
            DriveInformationCollection = database.GetCollection<DriveInformation>(DriveInformation);
            DirectoryInformationCollection = database.GetCollection<DirectoryInformation>(DirectoryInformation);
            FileInformationCollection = database.GetCollection<FileInformation>(FileInformation);
            FileAttributeInformationCollection = database.GetCollection<FileAttributeInformation>(FileAttributeInformation);
        }

        public static async Task CreateTables() {
            await DirectoryInformationCollection.Indexes.CreateOneAsync(new CreateIndexModel<DirectoryInformation>(Builders<DirectoryInformation>
                .IndexKeys.Ascending((item) => item.DriveId)
                .Ascending((item) => item.Path)));

            await FileInformationCollection.Indexes.CreateOneAsync(new CreateIndexModel<FileInformation>(Builders<FileInformation>
                .IndexKeys.Ascending((item) => item.DirectoryId)
                .Ascending((item) => item.DriveId)));
        }

        public static async Task<Guid> GetDirectoryId(DriveInformation drive, DirectoryInfo directory) {

            var directoryPath = directory.ToDirectoryPath()?.Trim().ToLower();

            var filter = Builders<DirectoryInformation>.Filter.Where(item => item.DriveId == drive.Id && item.Path == directoryPath);

            var result = await (await DirectoryInformationCollection.FindAsync(filter)).FirstOrDefaultAsync();

            if (result != null) {
                return result.Id;
            }

            Guid? parentDirectoryInfo = null;

            if (directory.Parent != null) {
                var parentName = directory.Parent.FullName.Substring(3);
                parentDirectoryInfo = await GetDirectoryId(drive, directory.Parent);
            }

            var directoryName = directory.Name;

            if (directoryName.IndexOf(":") > -1) {
                directoryName = directoryName.Substring(3);
            }

            //create a new record
            var newDirectory = new DirectoryInformation() {
                DriveId = drive.Id,
                Name = directoryName,
                ParentDirectoryId = parentDirectoryInfo,
                Path = directoryPath
            };

            await DirectoryInformationCollection.InsertOneAsync(newDirectory);

            return newDirectory.Id;
        }

    }

}
