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
using SQLite;

namespace FileParser.Data {
    
    public static class DriveUtilities {


        public static List<DriveInformation> ProcessDriveList(SQLiteConnection db) {
            db.BeginTransaction();

            var cmd = db.CreateCommand("Select * from DriveInformation");

            var hdCollection = cmd.ExecuteQuery<DriveInformation>();

            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");

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

            //var di = new DriveInfo(drive);

            //return the serial number
            return (disk["VolumeSerialNumber"] ?? string.Empty).ToString();
        }
    }
}
