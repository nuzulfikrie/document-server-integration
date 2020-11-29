﻿/**
 *
 * (c) Copyright Ascensio System SIA 2020
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Configuration;
using System.Web.Script.Serialization;
using System.Web.UI;
using ASC.Api.DocumentConverter;

namespace OnlineEditorsExample
{
    internal static class FileType
    {
        public static readonly List<string> ExtsSpreadsheet = new List<string>
            {
                ".xls", ".xlsx", ".xlsm",
                ".xlt", ".xltx", ".xltm",
                ".ods", ".fods", ".ots", ".csv"
            };

        public static readonly List<string> ExtsPresentation = new List<string>
            {
                ".pps", ".ppsx", ".ppsm",
                ".ppt", ".pptx", ".pptm",
                ".pot", ".potx", ".potm",
                ".odp", ".fodp", ".otp"
            };

        public static readonly List<string> ExtsDocument = new List<string>
            {
                ".doc", ".docx", ".docm",
                ".dot", ".dotx", ".dotm",
                ".odt", ".fodt", ".ott", ".rtf", ".txt",
                ".html", ".htm", ".mht",
                ".pdf", ".djvu", ".fb2", ".epub", ".xps"
            };

        public static string GetInternalExtension(string extension)
        {
            extension = Path.GetExtension(extension).ToLower();
            if (ExtsDocument.Contains(extension)) return ".docx";
            if (ExtsSpreadsheet.Contains(extension)) return ".xlsx";
            if (ExtsPresentation.Contains(extension)) return ".pptx";
            return string.Empty;
        }
    }

    public partial class _Default : Page
    {
        public static UriBuilder Host
        {
            get
            {
                var uri = new UriBuilder(HttpContext.Current.Request.Url) {Query = ""};
                var requestHost = HttpContext.Current.Request.Headers["Host"];
                if (!string.IsNullOrEmpty(requestHost))
                    uri = new UriBuilder(uri.Scheme + "://" + requestHost);

                return uri;
            }
        }

        public static string VirtualPath
        {
            get
            {
                return
                    HttpRuntime.AppDomainAppVirtualPath
                    + (HttpRuntime.AppDomainAppVirtualPath.EndsWith("/") ? "" : "/")
                    + WebConfigurationManager.AppSettings["storage-path"]
                    + CurUserHostAddress(null) + "/";
            }
        }

        private static bool? _ismono;

        public static bool IsMono
        {
            get { return _ismono.HasValue ? _ismono.Value : (_ismono = (bool?)(Type.GetType("Mono.Runtime") != null)).Value; }
        }

        private static long MaxFileSize
        {
            get
            {
                long size;
                long.TryParse(WebConfigurationManager.AppSettings["filesize-max"], out size);
                return size > 0 ? size : 5*1024*1024;
            }
        }

        private static List<string> FileExts
        {
            get { return ViewedExts.Concat(EditedExts).Concat(ConvertExts).ToList(); }
        }

        private static List<string> ViewedExts
        {
            get { return (WebConfigurationManager.AppSettings["files.docservice.viewed-docs"] ?? "").Split(new char[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
        }

        public static List<string> EditedExts
        {
            get { return (WebConfigurationManager.AppSettings["files.docservice.edited-docs"] ?? "").Split(new char[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
        }

        public static List<string> ConvertExts
        {
            get { return (WebConfigurationManager.AppSettings["files.docservice.convert-docs"] ?? "").Split(new char[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
        }

        private static string _fileName;

        public static string CurUserHostAddress(string userAddress)
        {
            return Regex.Replace(userAddress ?? HttpContext.Current.Request.UserHostAddress, "[^0-9a-zA-Z.=]", "_");
        }

        public static string StoragePath(string fileName, string userAddress)
        {
            var directory = HttpRuntime.AppDomainAppPath + WebConfigurationManager.AppSettings["storage-path"] + CurUserHostAddress(userAddress) + "\\";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return directory + fileName;
        }

        public static string HistoryDir(string storagePath)
        {
            return storagePath += "-hist";
        }

        public static string VersionDir(string histPath, int version)
        {
            return Path.Combine(histPath, version.ToString());
        }

        public static string VersionDir(string fileName, string userAddress, int version)
        {
            return VersionDir(HistoryDir(StoragePath(fileName, userAddress)), version);
        }

        public static int GetFileVersion(string historyPath)
        {
            if (!Directory.Exists(historyPath)) return 0;
            return Directory.EnumerateDirectories(historyPath).Count();
        }

        public static int GetFileVersion(string fileName, string userAddress)
        {
            return GetFileVersion(HistoryDir(StoragePath(fileName, userAddress)));
        }

        public static string FileUri(string fileName)
        {
            var uri = Host;
            uri.Path = VirtualPath + fileName;
            return uri.ToString();
        }

        public static string DocumentType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLower();

            if (FileType.ExtsDocument.Contains(ext)) return "text";
            if (FileType.ExtsSpreadsheet.Contains(ext)) return "spreadsheet";
            if (FileType.ExtsPresentation.Contains(ext)) return "presentation";

            return string.Empty;
        }

        protected string UrlPreloadScripts = WebConfigurationManager.AppSettings["files.docservice.url.preloader"];


        protected void Page_Load(object sender, EventArgs e)
        {
        }

        public static string DoUpload(HttpContext context)
        {
            var httpPostedFile = context.Request.Files[0];

            if (HttpContext.Current.Request.Browser.Browser.ToUpper() == "IE")
            {
                var files = httpPostedFile.FileName.Split(new char[] { '\\' });
                _fileName = files[files.Length - 1];
            }
            else
            {
                _fileName = httpPostedFile.FileName;
            }

            var curSize = httpPostedFile.ContentLength;
            if (MaxFileSize < curSize || curSize <= 0)
            {
                throw new Exception("File size is incorrect");
            }

            var curExt = (Path.GetExtension(_fileName) ?? "").ToLower();
            if (!FileExts.Contains(curExt))
            {
                throw new Exception("File type is not supported");
            }

            _fileName = GetCorrectName(_fileName);

            var savedFileName = StoragePath(_fileName, null);
            httpPostedFile.SaveAs(savedFileName);

            var histDir = HistoryDir(savedFileName);
            Directory.CreateDirectory(histDir);
            File.WriteAllText(Path.Combine(histDir, "createdInfo.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "created", DateTime.Now.ToString() },
                { "id", context.Request.Cookies.GetOrDefault("uid", "uid-1") },
                { "name", context.Request.Cookies.GetOrDefault("uname", "John Smith") }
            }));

            return _fileName;
        }

        public static string DoUpload(string fileUri, HttpRequest request)
        {
            _fileName = GetCorrectName(Path.GetFileName(fileUri));

            var curExt = (Path.GetExtension(_fileName) ?? "").ToLower();
            if (!FileExts.Contains(curExt))
            {
                throw new Exception("File type is not supported");
            }

            var req = (HttpWebRequest)WebRequest.Create(fileUri);

            try
            {
                // hack. http://ubuntuforums.org/showthread.php?t=1841740
                if (IsMono)
                {
                    ServicePointManager.ServerCertificateValidationCallback += (s, ce, ca, p) => true;
                }

                using (var stream = req.GetResponse().GetResponseStream())
                {
                    if (stream == null) throw new Exception("stream is null");
                    const int bufferSize = 4096;

                    using (var fs = File.Open(StoragePath(_fileName, null), FileMode.Create))
                    {
                        var buffer = new byte[bufferSize];
                        int readed;
                        while ((readed = stream.Read(buffer, 0, bufferSize)) != 0)
                        {
                            fs.Write(buffer, 0, readed);
                        }
                    }
                }

                var histDir = HistoryDir(StoragePath(_fileName, null));
                Directory.CreateDirectory(histDir);
                File.WriteAllText(Path.Combine(histDir, "createdInfo.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                    { "created", DateTime.Now.ToString() },
                    { "id", request.Cookies.GetOrDefault("uid", "uid-1") },
                    { "name", request.Cookies.GetOrDefault("uname", "John Smith") }
                }));
            }
            catch (Exception)
            {

            }
            return _fileName;
        }

        public static string DoConvert(HttpContext context)
        {
            _fileName = context.Request["filename"];

            var extension = (Path.GetExtension(_fileName) ?? "").Trim('.');
            var internalExtension = FileType.GetInternalExtension(_fileName).Trim('.');

            if (ConvertExts.Contains("." + extension)
                && !string.IsNullOrEmpty(internalExtension))
            {
                var key = ServiceConverter.GenerateRevisionId(FileUri(_fileName));

                string newFileUri;
                var result = ServiceConverter.GetConvertedUri(FileUri(_fileName), extension, internalExtension, key, true, out newFileUri);
                if (result != 100)
                {
                    return "{ \"step\" : \"" + result + "\", \"filename\" : \"" + _fileName + "\"}";
                }

                var fileName = GetCorrectName(Path.GetFileNameWithoutExtension(_fileName) + "." + internalExtension);

                var req = (HttpWebRequest)WebRequest.Create(newFileUri);

                // hack. http://ubuntuforums.org/showthread.php?t=1841740
                if (IsMono)
                {
                    ServicePointManager.ServerCertificateValidationCallback += (s, ce, ca, p) => true;
                }

                using (var stream = req.GetResponse().GetResponseStream())
                {
                    if (stream == null) throw new Exception("Stream is null");
                    const int bufferSize = 4096;

                    using (var fs = File.Open(StoragePath(fileName, null), FileMode.Create))
                    {
                        var buffer = new byte[bufferSize];
                        int readed;
                        while ((readed = stream.Read(buffer, 0, bufferSize)) != 0)
                        {
                            fs.Write(buffer, 0, readed);
                        }
                    }
                }

                var storagePath = StoragePath(_fileName, null);
                var histDir = HistoryDir(storagePath);
                File.Delete(storagePath);
                if (Directory.Exists(histDir)) Directory.Delete(histDir, true);

                _fileName = fileName;
                histDir = HistoryDir(StoragePath(_fileName, null));
                Directory.CreateDirectory(histDir);
                File.WriteAllText(Path.Combine(histDir, "createdInfo.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                    { "created", DateTime.Now.ToString() },
                    { "id", context.Request.Cookies.GetOrDefault("uid", "uid-1") },
                    { "name", context.Request.Cookies.GetOrDefault("uname", "John Smith") }
                }));
            }

            return "{ \"filename\" : \"" + _fileName + "\"}";
        }

        public static string GetCorrectName(string fileName, string userAddress = null)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var name = baseName + ext;

            for (var i = 1; File.Exists(StoragePath(name, userAddress)); i++)
            {
                name = baseName + " (" + i + ")" + ext;
            }
            return name;
        }
        public static Dictionary<string, Dictionary<string, object>> GetFileInfo()
        {
            var result = new Dictionary<string, Dictionary<string, object>>();
            var storedFiles = GetStoredFiles();
            if (storedFiles.Any())
            {
                foreach (object storedfile in storedFiles)
                {
                    var fileName = storedfile.ToString();
                    var key = ServiceConverter.GenerateRevisionId(CurUserHostAddress(null)
                                                           + "/" + Path.GetFileName(FileUri(fileName))
                                                           + "/" + File.GetLastWriteTime(StoragePath(fileName, null)).GetHashCode());
                    var fileinf = new FileInfo(fileName);
                    var tmp = new Dictionary<string, object>
                    {
                         { "version", GetFileVersion(HistoryDir(StoragePath(fileName,null))) },
                         {  "id" , key },
                         { "title" , fileName },
                         { "pureContentLength" , fileinf.Length },
                         { "contentLength" , BytesToString(fileinf.Length) },
                         {  "updated" , fileinf.LastWriteTime }
                     };
                    result.Add(fileName, tmp);
                }
            }
            return result;
        }

        public static String BytesToString(long byteCount)
        {
            string[] suf = { "Byt", "KB", "MB", "GB", "TB", "PB", "EB" }; //
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }
        protected static List<string> GetStoredFiles()
        {
            var directory = HttpRuntime.AppDomainAppPath + WebConfigurationManager.AppSettings["storage-path"] + CurUserHostAddress(null) + "\\";
            if (!Directory.Exists(directory)) return new List<string>();

            var directoryInfo = new DirectoryInfo(directory);

            var storedFiles = directoryInfo.GetFiles("*.*", SearchOption.TopDirectoryOnly).Select(fileInfo => fileInfo.Name).ToList();
            return storedFiles;
        }
    }
}