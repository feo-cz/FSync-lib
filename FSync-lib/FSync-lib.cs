using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.IO.Compression;
using Ionic.Zip;


namespace FSync_lib
{
    public class FSync_lib
    {
        static bool allowCreateSubdirectories   = false;
        static bool allowOverwriteExistingFiles = false;
        static string ZIPfileSuffixName         = "";
        static int ZIPpartSizeMB                = 0;
        static string listOfAllowedExtensions   = @"^.+\.*$";
        static string indexFileName             = "";
        static string sourceDirectory           = "";
        static string destinationDirectory      = "";
        static string pathForLogFile            = "";

        public static void run()
        {
            Logger.Instance.setPath(pathForLogFile);
            Logger.Instance.Log("--------------------------------------------------------------------------");
            Logger.Instance.Log("START FSync " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            Logger.Instance.Log("--------------------------------------------------------------------------");
            if (sourceDirectory == "")
            {
                Logger.Instance.Log("Missing parameter SRCDIR");
                return;
            }

            if (!Directory.Exists(sourceDirectory))
            {
                Logger.Instance.Log("Directory '" + sourceDirectory + "' doesn't exist!");
                return;
            }

            if (destinationDirectory == "")
            {
                Logger.Instance.Log("Missing parameter DESTDIR");
                return;
            }

            Logger.Instance.Log("Moved from " + sourceDirectory);
            Logger.Instance.Log("Moved to " + destinationDirectory);

            if(ZIPpartSizeMB > 0)
            {
                Logger.Instance.Log("ZIP file split to parts (" + ZIPpartSizeMB + " MB each).");
            }

            FileInfo[] files = GetFilesFromDir(sourceDirectory, listOfAllowedExtensions);

            IndexFiles(files);

            if (ZIPfileSuffixName != "")
            {
                bool isCreatedCompressFile = compressFilesToZip();

                if(isCreatedCompressFile)
                {
                    Logger.Instance.Log("Created compress file " + ZIPfileSuffixName);
                }
            }

            CommitIndex();

            Logger.Instance.Log("--------------------------------------------------------------------------");
            Logger.Instance.Log("STOP FSync " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            Logger.Instance.Log("--------------------------------------------------------------------------");
            Logger.Instance.Log("\n\r");

            return;

        }

        private static FileInfo[] GetFilesFromDir(string directory, string extensions = @"^.+\.*$")
        {
            var filePaths = Directory.GetFiles(directory, "*.*").Where(file => Regex.IsMatch(file, extensions));
            if (allowCreateSubdirectories == true)
            {
                filePaths = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories).Where(file => Regex.IsMatch(file, extensions));
            }

            List<FileInfo> filesList = new List<FileInfo>();
            foreach (string fileName in filePaths)
            {
                try
                {
                    FileInfo fi1 = new FileInfo(fileName);
                    filesList.Add(fi1);

                    // Logger.Instance.Log("Added file with name " + fi1.Name);
                } catch (IOException ioex)
                {

                }
            }

            FileInfo[] files = filesList.ToArray();

            return files;
        }

        private static bool moveFile(FileInfo file)
        {
            if (!file.Exists)
            {
                return false;
            }

            if(!IsFileReadable(file))
            {
                return false;
            }

            if (ZIPfileSuffixName != "")
            {
                if (!Directory.Exists(destinationDirectory + "tmp"))
                {
                    DirectoryInfo dir = Directory.CreateDirectory(destinationDirectory + "tmp");
                    dir.Attributes = FileAttributes.Directory | FileAttributes.Hidden;

                }

                try
                {
                    file.CopyTo(destinationDirectory + "tmp\\" + file.Name, allowOverwriteExistingFiles);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException))
                    {
                        throw;
                    }

                    Logger.Instance.Log(file.FullName + ": " + ex.Message);

                    return false;
                }
            }
            else
            {
                string filePath = destinationDirectory + file.FullName.Replace(sourceDirectory, "");
                string directory = destinationDirectory.Substring(0, destinationDirectory.Length - 1) + file.DirectoryName.Replace(sourceDirectory.Substring(0, sourceDirectory.Length - 1), "");
                if (!Directory.Exists(directory))
                {
                    DirectoryInfo di = Directory.CreateDirectory(directory);
                }
                try
                {
                    file.CopyTo(filePath, allowOverwriteExistingFiles);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException))
                    {
                        throw;
                    }

                    Logger.Instance.Log(file.FullName + ": " + ex.Message);

                    return false;
                }
            }

            return true;
        }

        private static bool IsFileReadable(FileInfo file)
        {
            /*Console.WriteLine(file.Name);
            try
            {
                using (FileStream stream = File.Open(file.Name, FileMode.Open, FileAccess.Read))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }*/
            return true;
        }

        private static string IndexPath()
        {
            return sourceDirectory + indexFileName;
        }

        private static string NewIndexPath()
        {
            return IndexPath() + ".new";
        }

        // index is swapped in only after a finished run, so an interrupted run keeps the old one
        private static void IndexFiles(FileInfo[] files)
        {
            int countMoveFiles = 0;

            Dictionary<string, string> dataFromIndex = getDataFromIndex();

            string newIndexFileName = indexFileName + ".new";

            using (StreamWriter sw = new StreamWriter(NewIndexPath()))
            {
                foreach (FileInfo file in files)
                {
                    if (file.Name == indexFileName || file.Name == newIndexFileName)
                    {
                        continue;
                    }

                    string time = GetFileTimestamp(file).ToString();

                    if (isNewFile(dataFromIndex, file.FullName, time))
                    {
                        if (!moveFile(file))
                        {
                            // not indexed, so the transfer is retried next run
                            continue;
                        }

                        countMoveFiles++;
                    }

                    sw.WriteLine(file.FullName + "|" + time);
                }

                sw.Flush();
            }

            Logger.Instance.Log("Moved " + countMoveFiles + " files.");
        }

        private static void CommitIndex()
        {
            if (File.Exists(IndexPath()))
            {
                try
                {
                    File.Replace(NewIndexPath(), IndexPath(), null);

                    return;
                }
                catch (Exception ex)
                {
                    // File.Replace is not supported on some network shares
                    if (!(ex is IOException || ex is PlatformNotSupportedException))
                    {
                        throw;
                    }
                }

                File.Delete(IndexPath());
            }

            File.Move(NewIndexPath(), IndexPath());
        }

        private static int GetFileTimestamp(FileInfo file)
        {
            int time = DateTimeToUnixTimestamp(file.LastWriteTimeUtc);
            int creationTime = DateTimeToUnixTimestamp(file.CreationTimeUtc);

            return creationTime > time ? creationTime : time;
        }

        private static bool compressFilesToZip()
        {
            if (Directory.Exists(destinationDirectory + "tmp"))
            {
                int segmentsCreated = 0;
                using (Ionic.Zip.ZipFile zip = new Ionic.Zip.ZipFile(Encoding.UTF8))
                {
                    if (ZIPpartSizeMB <= 0)
                    {
                        zip.AddDirectory(destinationDirectory + "tmp");
                        zip.Comment = "This zip was created at " + System.DateTime.Now.ToString("G");

                        zip.Save(destinationDirectory + ZIPfileSuffixName);

                        segmentsCreated = zip.NumberOfSegmentsForMostRecentSave;
                    }
                }

                if (ZIPpartSizeMB > 0)
                {

                    FileInfo[] files = GetFilesFromDir(destinationDirectory + "tmp", listOfAllowedExtensions);


                    double filesSize = 0;
                    int part = 0;
                    int lastIndex = 0;

                    while (true)
                    {
                        using (Ionic.Zip.ZipFile zip = new Ionic.Zip.ZipFile(Encoding.UTF8))
                        {
                            if (lastIndex == files.Count())
                            {
                                segmentsCreated++;

                                break;
                            }

                            for (int i = lastIndex; i <= files.Count(); i++)
                            {
                                if(lastIndex == files.Count())
                                {
                                    part++;
                                    zip.Save(destinationDirectory + part + "_" + ZIPfileSuffixName);

                                    filesSize = 0;

                                    break;
                                }

                                FileInfo file = files[i];

                                if(file.Length / 1024 > ZIPpartSizeMB * 1024)
                                {
                                    zip.AddFile(file.FullName, "");

                                    filesSize += file.Length / 1024;
                                    lastIndex++;

                                    part++;
                                    zip.Save(destinationDirectory + part + "_" + ZIPfileSuffixName);

                                    filesSize = 0;

                                    break;
                                }


                                if ((ZIPpartSizeMB * 1024 > (filesSize + (file.Length / 1024)) && i < files.Count()))
                                {
                                    zip.AddFile(file.FullName, "");

                                    filesSize += file.Length / 1024;
                                    lastIndex++;
                                }
                                else
                                {
                                    part++;
                                    zip.Save(destinationDirectory + part + "_" + ZIPfileSuffixName);

                                    filesSize = 0;

                                    break;
                                }
                            }
                        }
                    }
                }

                if (segmentsCreated > 0)
                {
                    try
                    {
                        DeleteDirectory(destinationDirectory + "tmp");
                    } catch (IOException ioex)
                    {

                    }

                    return true;
                }
            }

            return false;
        }

        private static void DeleteDirectory(string targetDir)
        {
            File.SetAttributes(targetDir, FileAttributes.Normal);

            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(targetDir, false);
        }

        private static Dictionary<string, string> getDataFromIndex()
        {
            Dictionary<string, string> filesList = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!File.Exists(IndexPath()))
            {
                return filesList;
            }

            using (StreamReader sr = new StreamReader(IndexPath()))
            {
                string s;
                while ((s = sr.ReadLine()) != null)
                {
                    int separator = s.LastIndexOf('|');
                    if (separator < 0)
                    {
                        continue;
                    }

                    filesList[s.Substring(0, separator)] = s.Substring(separator + 1);
                }
            }

            return filesList;
        }

        private static bool isNewFile(Dictionary<string, string> filesFromIndex, string fullName, string time)
        {
            string indexedTime;

            return !filesFromIndex.TryGetValue(fullName, out indexedTime) || indexedTime != time;
        }

        private static void createDirectory(string directoryName)
        {
            if (!System.IO.Directory.Exists(directoryName))
            {
                System.IO.Directory.CreateDirectory(directoryName);
            }
        }

        public static int DateTimeToUnixTimestamp(DateTime dateTime)
        {
            DateTime unixStart = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            long unixTimeStampInTicks = (dateTime.ToUniversalTime() - unixStart).Ticks;
            return (int)((double)unixTimeStampInTicks / TimeSpan.TicksPerSecond);
        }

        public static void setAllowCreateSubdirectories(bool allowCreateSubdirectoriesParam)
        {
            allowCreateSubdirectories = allowCreateSubdirectoriesParam;
        }

        public static void setAllowOverwriteExistingFiles(bool allowOverwriteExistingFilesParam)
        {
            allowOverwriteExistingFiles = allowOverwriteExistingFilesParam;
        }

        public static void setZIPfileSuffixName(string ZIPfileSuffixNameParam)
        {
            ZIPfileSuffixName = ZIPfileSuffixNameParam;
        }

        public static void setZIPpartSizeMB(int ZIPpartSizeMBParam)
        {
            ZIPpartSizeMB = ZIPpartSizeMBParam;
        }

        public static void setListOfAllowedExtensions(string listOfAllowedExtensionsParam)
        {
            listOfAllowedExtensions = listOfAllowedExtensionsParam;
        }

        public static void setIndexFileName(string indexFileNameParam)
        {
            indexFileName = indexFileNameParam;
        }

        public static void setSourceDirectory(string sourceDirectoryParam)
        {
            if (sourceDirectoryParam == "")
            {
                return;
            }

            int at = sourceDirectoryParam.LastIndexOf('\\', sourceDirectoryParam.Length - 1, 1);

            if (at < 0)
            {
                sourceDirectoryParam = sourceDirectoryParam + "\\";
            }

            sourceDirectory = sourceDirectoryParam;
        }

        public static void setDestinationDirectory(string destinationDirectoryParam)
        {
            if (destinationDirectoryParam == "")
            {
                return;
            }

            int at = destinationDirectoryParam.LastIndexOf('\\', destinationDirectoryParam.Length - 1, 1);

            if (at < 0)
            {
                destinationDirectoryParam = destinationDirectoryParam + "\\";
            }

            destinationDirectory = destinationDirectoryParam;

            if (destinationDirectory != "")
            {
                createDirectory(destinationDirectory);
            }
        }

        public static void setPathForLogFile(string pathForLogFileParam)
        {
            if (pathForLogFileParam == "")
            {
                return;
            }

            int at = pathForLogFileParam.LastIndexOf('\\', pathForLogFileParam.Length - 1, 1);

            if (at < 0)
            {
                pathForLogFileParam = pathForLogFileParam + "\\";
            }

            pathForLogFile = pathForLogFileParam;

            if (pathForLogFile != "")
            {
                createDirectory(pathForLogFile);
            }
        }
    }
}
