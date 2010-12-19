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
using System.Diagnostics;
using SQLite;

namespace FileParser.Data {

    [DebuggerDisplay("DriveId = {DriveId} DriveLetter = {DriveLetter} SerialNo = {SerialNo}  VolumeName = {VolumeName}  TotalSize = {TotalSize}  Model = {Model}")]
    public class DriveInformation {

        [AutoIncrement]
        [PrimaryKey]
        public int DriveId {
            get;
            set;
        }

        public string DriveType {
            get;
            set;
        }

        public string DriveLetter {
            get;
            set;
        }

        public string Model {
            get;
            set;
        }

        public string SerialNo {
            get;
            set;
        }

        //TODO see if a default value exists in sqlite and add it if it does not.

        public long TotalSize {
            get;
            set;
        }

        public string VolumeName {
            get;
            set;
        }

    }

}
