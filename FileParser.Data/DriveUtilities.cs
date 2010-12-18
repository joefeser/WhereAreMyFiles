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

namespace FileParser.Data {
    
    public static class DriveUtilities {

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
