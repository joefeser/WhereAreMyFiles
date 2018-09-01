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
using System.Management;
using MongoDB.Driver;
using System.Threading.Tasks;
using System.Reflection;

namespace FileParser.Data {

    public static class DriveUtilities {

        public static List<string> GetFileAttributeList() {
            //TODO determine if we need to use the drive we are on or can we use any folder. Also can this list change?
            var arrHeaders = new List<string>();

            var folder = GetShell32Folder(@"C:\");

            for (int i = 0; i < short.MaxValue; i++) {
                string header = folder.GetDetailsOf(null, i);
                if (String.IsNullOrEmpty(header))
                    break;
                arrHeaders.Add(header);
            }
            return arrHeaders;
        }

        public static Shell32.Folder GetShell32Folder(string folderPath) {
            Type shellAppType = Type.GetTypeFromProgID("Shell.Application");
            Object shell = Activator.CreateInstance(shellAppType);
            return (Shell32.Folder)shellAppType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { folderPath });
        }

        public static async Task<List<DriveInformation>> ProcessDriveList() {
            var hdCollection = await (await DatabaseLookups.DriveInformationCollection.FindAsync(Builders<DriveInformation>.Filter.Empty)).ToListAsync();

            ManagementObjectSearcher searcher = null;

            searcher = new ManagementObjectSearcher("Select * from Win32_LogicalDisk");
            var moc = searcher.Get();

            foreach (ManagementObject mo in moc) {
                string driveLetter = mo["DeviceId"].ToString().Substring(0, 1);
                var volumeName = (mo["VolumeName"] ?? string.Empty).ToString().Trim();
                var volumeSerialNumber = (mo["VolumeSerialNumber"] ?? string.Empty).ToString().Trim();
                var driveType = mo["DriveType"].ToString();

                var hd = hdCollection.FirstOrDefault(item => item.DriveLetter == driveLetter);

                //what we want to do is find the item with the same drive letter and determine if it is actually the same.

                bool add = false;

                var driveInfo = new System.IO.DriveInfo(driveLetter + @":\");
                var totalSize = driveInfo.IsReady ? driveInfo.TotalSize : 0;

                //try to determine if we have the correct record
                if (hd != null) {
                    if (!string.IsNullOrWhiteSpace(volumeSerialNumber)) {
                        if (hd.SerialNo != volumeSerialNumber) {
                            var tempHd = hdCollection.FirstOrDefault(item => item.SerialNo == volumeSerialNumber);
                            if (tempHd == null) {
                                add = true;
                            }
                            else {
                                hd = tempHd;
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(volumeName)) {
                        if (hd.VolumeName != volumeName) {
                            var tempHd = hdCollection.FirstOrDefault(item => item.VolumeName == volumeName && item.TotalSize == totalSize);
                            if (tempHd == null) {
                                add = true;
                            }
                            else {
                                hd = tempHd;
                            }
                        }
                    }
                }

                //drive letters can change.
                if (hd == null && !string.IsNullOrWhiteSpace(volumeSerialNumber)) {
                    hd = hdCollection.FirstOrDefault(item => item.SerialNo == volumeSerialNumber);
                }
                if (hd == null && !string.IsNullOrWhiteSpace(volumeName)) {
                    hd = hdCollection.FirstOrDefault(item => item.VolumeName == volumeName && item.TotalSize == totalSize);
                }

                if (add && hd != null) {
                    hdCollection.Remove(hd);
                }

                if (hd == null || add) {
                    hd = new DriveInformation();
                    hdCollection.Add(hd);
                    hd.DriveLetter = driveLetter;
                    hd.DriveType = driveType;
                    hd.SerialNo = volumeSerialNumber;
                    hd.TotalSize = totalSize;
                    hd.VolumeName = volumeName;
                    await DatabaseLookups.DriveInformationCollection.InsertOneAsync(hd);
                }
                else {
                    hd.DriveLetter = driveLetter;
                    hd.DriveType = driveType;
                    hd.SerialNo = volumeSerialNumber;
                    hd.TotalSize = totalSize;
                    hd.VolumeName = volumeName;
                    await DatabaseLookups.DriveInformationCollection.ReplaceOneAsync(Builders<DriveInformation>.Filter.Where(item => item.Id == hd.Id), hd);
                }
                //now we need to remove any drive with the same letter that is not the current one.

                foreach (var item in hdCollection.Where(h => h.DriveLetter.Equals(driveLetter, StringComparison.OrdinalIgnoreCase)).ToList()) {
                    if (item != hd) {
                        hdCollection.Remove(item);
                    }
                }
            }

            searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject wmi_HD in searcher.Get()) {

                var query = "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + wmi_HD["DeviceID"] + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition";

                var searcher2 = new ManagementObjectSearcher(query);

                foreach (ManagementObject association in searcher2.Get()) {

                    query = "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + association["DeviceID"] + "'} WHERE AssocClass = Win32_LogicalDiskToPartition";
                    var searcher3 = new ManagementObjectSearcher(query);
                    foreach (ManagementObject driveLetter in searcher3.Get()) {
                        //we want one added for every drive letter on the disk.
                        var dl = driveLetter["DeviceID"].ToString().Substring(0, 1);
                        var hd = hdCollection.FirstOrDefault(drive => drive.DriveLetter == dl);

                        //only perform updates
                        if (hd != null) {
                            hd.Model = wmi_HD["Model"]?.ToString();
                            hd.DriveType = wmi_HD["InterfaceType"]?.ToString();
                            await DatabaseLookups.DriveInformationCollection.ReplaceOneAsync(Builders<DriveInformation>.Filter.Where(item => item.Id == hd.Id), hd);
                        }
                    }
                }
            }
            return hdCollection;
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

            //return the serial number
            return (disk["VolumeSerialNumber"] ?? string.Empty).ToString();
        }
    }
}
