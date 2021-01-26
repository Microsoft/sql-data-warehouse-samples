/**********************************************************************************************************************************************************************************
*
*       Solution:   DBLoader
*       Module:     Program
*       Copyright:  Copyright © 2018 Mitch van Huuksloot, Microsoft Corporation
*       Author:     Mitch van Huuksloot, Microsoft Corporation
*       Support:    This utility comes with no warranty of any kind, either express or implied. Neither Microsoft nor the author make any warranty of fitness for any purpose. 
*       Purpose:    Provides main entry point into the application, argument processing, input path or file processing and parallel Task handling.
* 
***********************************************************************************************************************************************************************************/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Rest;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.Azure.DataLake.Store;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

namespace DBLoader
{
    class Program
    {
        #region Class Statics and Variables
        const string helpstr =  "DBLoader Utility - Copyright © 2018 Mitch van Huuksloot, Microsoft Corporation\n" +
                                "Console utility that imports text files with large text columns into SQLServer.\n"+
                                "The text file can reside on local storage, Azure Blob or Data Lake Stores\n\n" +
                                "Command: DBLoader <path><file name> -S<schema> -T<tablename>\n\n" +
                                "File name (can contain wildcards) and table name are mandatory\n" +
                                "Path is optional, current directory will be used.\n" +
                                "Schema is optional, if not given dbo will be used.\n" +
                                "The application config file is used to specify the database connection string, Azure credentials, column separator(s) and string qualifiers etc.\n" +
                                "Examples:\n"+
                                "DBLoader c:\\temp\\myfiles -Tmytable\n"+
                                "DBLoader asb:/<container>/*.gz -Smyschema -Tmytable\n"+
                                "DBLoader adl:/<folder path>/*.gz -Smyschema -Tmytable\n";

        static int files = 0;
        static Task<fileprocstats>[] tasks;                                                     // process files in multiple Tasks (threads) - let user configure the number of threads
        #endregion

        static bool ProcessInputArguments(ref string[] args)
        {
            for (int arg = 0; arg < args.Length; arg++)
            {
                if (args[arg].StartsWith("?"))
                {
                    Console.WriteLine(helpstr);
                    return false;
                }
                if (!(args[arg].StartsWith("-") || args[arg].StartsWith("/") || args[arg].StartsWith("\\")))
                {
                    if (globals.path == "") globals.path = args[arg];
                    else
                    {
                        Console.WriteLine("Unknown command line argument " + args[arg]);
                        return false;
                    }
                }
                else
                    switch (args[arg].Substring(1, 1).ToUpper())
                    {
                        case "T":
                            globals.tablename = args[arg].Substring(2);
                            break;
                        case "S":
                            globals.schema = args[arg].Substring(2);
                            break;
                        case "?":
                            Console.WriteLine(helpstr);
                            return false;
                        default:
                            Console.WriteLine("Unknown command line option: " + args[arg]);
                            return false;
                    }
            }

            if (globals.path.Length == 0 || globals.tablename.Length == 0)
            {
                Console.WriteLine("A file name and table name (using the -T option) must be provided. Use ? or /? for help.");
                return false;
            }
            return true;
        }

        static void ProcessTaskStatus(Task<fileprocstats> t)
        {
            if (t.IsFaulted)
            {
                foreach (var e in t.Exception.InnerExceptions)
                {
                    Console.WriteLine("Import Task had an exception: " + e.Message);
                }
            }
            else if (t.IsCompleted)                                                             // if task completed successfully
            {
                fileprocstats fps = t.Result;
                globals.totallinesread += fps.linesread;
                globals.totallineswritten += fps.lineswritten;
            }
        }

        static void WaitForTaskComplete()
        {
#if !DEBUG
            // wait for all tasks to complete and process the statuses (counts) - tasks are only used in the release build, so for debug this is a no-op
            for (int i = 0; i < globals.threads; i++)
            {
                if (tasks[i] != null)
                {
                    tasks[i].Wait();
                    ProcessTaskStatus(tasks[i]);
                }
            }
#endif
        }

        static void ProcessFile(FileInfo f, Stream s, string fname)
        {
#if DEBUG
            files++;
            Console.WriteLine("Processing (" + files.ToString() + "): " + (f == null ? fname : f.Name));
            InputFile inpfile;                                                                              // to aid debugging - do not use the thread pool (Task) and do files one by one
            if (f != null) inpfile = new InputFile(f);
            else inpfile = new InputFile(s, fname);
            fileprocstats fps = inpfile.Process();
            globals.totallinesread += fps.linesread;
            globals.totallineswritten += fps.lineswritten;
#else
            int i = 0;

            while (true)
            {
                if (tasks[i] != null)                                                                       // check if task is complete
                {
                    if (tasks[i].IsCanceled || tasks[i].IsCompleted || tasks[i].IsFaulted)                  // did we get an error?
                    {
                        ProcessTaskStatus(tasks[i]);
                        tasks[i].Dispose();
                        tasks[i] = null;                                                                    // task is done, remove from array
                    }
                }
                if (tasks[i] == null)                                                                       // open task slot
                {
                    files++;
                    Console.WriteLine("Processing (" + files.ToString() + "): " + (f == null ? fname : f.Name));
                    tasks[i] = Task.Run<fileprocstats>(() => {  InputFile inpfile;
                        if (f != null) inpfile = new InputFile(f);
                        else inpfile = new InputFile(s, fname);
                        return inpfile.Process(); });
                    break;
                }
                if (++i >= globals.threads)
                {
                    i = 0;
                    Thread.Sleep(1000);
                }
            }
#endif
        }

        static bool ProcessPath()
        {
            string dir = Path.GetDirectoryName(globals.path);                                                   // surprisingly this works with ASB:/container/file - but changes / to \
            string fn = Path.GetFileName(globals.path);                                                         // extract just the filename
            bool wildcard = false;
            if (fn.Contains("*") || fn.Contains("%")) wildcard = true;                                          // if filename has wildcard characters need directory scan
            if (dir.ToUpper().StartsWith("ADL"))                                                                // special form to indicate Azure Data Lake - adl:\container\pattern
            {
                ServiceClientCredentials creds = ApplicationTokenProvider.LoginSilentAsync(globals.tenantId, globals.applicationId, globals.secretKey).Result;
                AdlsClient client = AdlsClient.CreateClient(globals.adlsAccountName, creds);
                if (wildcard)
                {
                    foreach (DirectoryEntry entry in client.EnumerateDirectory(dir.Substring(4, dir.Length-4).Replace('\\', '/').ToLower()))    // folder needs to be lower case, so fix it if necessary
                    {
                        if (entry.Name.ToLower().EndsWith(Path.GetExtension(globals.path).ToLower()))           // check if extension matches the pattern we want
                        {
                            ProcessFile(null, client.GetReadStream(entry.FullName), entry.Name);
                        }
                    }
                }
                else ProcessFile(null, client.GetReadStream(globals.path.Substring(4, globals.path.Length - 4).ToLower()), fn);                 // filename needs to be in lower case, correct if necessary
            }
            else if (dir.ToUpper().StartsWith("ASB"))                                                          // special form to indicate blob storage asb:\container\pattern
            {
                try
                {
                    CloudBlobClient blobClient = globals.csa.CreateCloudBlobClient();                           // get blob client using connection string in app.config
                    string[] folders = dir.Split('\\');                                                         // split the input file reference into folders
                    CloudBlobContainer container = blobClient.GetContainerReference(folders[1]);                // get container reference
                    string subfolder = "";
                    if (folders.Length > 2)
                        for (int j = 2; j < folders.Length; j++) subfolder += (j > 2 ? "/" : "") + folders[j];  // rebuild subfolder within container from its parts
                    if (wildcard)                                                                               // filename has a wildcard?
                    {
                        CloudBlobDirectory cd = container.GetDirectoryReference(subfolder);                     // get directory of subfolder user wants to process (name pattern, since containers don't have subfolders)    
                        foreach (CloudBlockBlob b in cd.ListBlobs(false, BlobListingDetails.Metadata))          // process each blob file
                        {
                            if (b.Name.ToLower().EndsWith(Path.GetExtension(globals.path).ToLower()))           // check if extension matches the pattern we want
                            {
                                ProcessFile(null, b.OpenRead(), b.Name);                                        // process this file
                            }
                        }
                    }
                    else
                    {
                        CloudBlob b = container.GetBlobReference(subfolder + "/" + fn);                         // get a reference to the specific file in the blob storage container
                        ProcessFile(null, b.OpenRead(), b.Name);                                                // process the file
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception accessing Azure blob storage: " + e.Message);
                    return false;
                }
            }
            else
            {
                if (wildcard)                                                                                   // file has a wildcard in it, so scan directory for matches
                {
                    DirectoryInfo di = null;

                    try
                    {
                        di = new DirectoryInfo(dir);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Folder specified was not found: " + globals.path);
                        return false;
                    }
                    foreach (FileInfo f in di.GetFiles(Path.GetFileName(globals.path)))
                        ProcessFile(f, null, "");
                }
                else
                {
                    FileInfo f = new FileInfo(globals.path);
                    if (f.Exists) ProcessFile(f, null, "");
                    else Console.WriteLine("File specified was not found: " + globals.path);
                }
            }
            return true;
        }

        static void Main(string[] args)
        {
            try
            {
                DateTime start = DateTime.Now;                                                                      // get app start time for elapse time calculation
                if (!ProcessInputArguments(ref args)) return;                                                       // process application arguments
                globals.LoadConfigValues();                                                                         // load configuration values from app.config
                tasks = new Task<fileprocstats>[globals.threads];                                                   // need threads set from config before allocating array size
                if (!globals.LoadColumMetadata()) return;                                                           // get table metadata from SQL Server
                int servoff = globals.constr.IndexOf("Server=") + 7;                                                // extract just SQL Server name from connection string
                string server = globals.constr.Substring(servoff, globals.constr.IndexOf(';', servoff) - servoff);
                Console.WriteLine("Loading: " + globals.path + " into SQL Server (" + server + ") table: " + globals.tablename);     // tell user what is going to happen (hopefully!)
                if (!ProcessPath()) return;                                                                         // process all files in the directory
                if (files == 0)                                                                                     // check that we actually did something
                {
                    Console.WriteLine("No matching files found in path provided");                                  // if no, we didn't find any files...
                    return;
                }
                WaitForTaskComplete();                                                                              // wait for all transfers to complete
                double elapsesec = DateTime.Now.Subtract(start).TotalSeconds;                                       // calculate elapsed run time in seconds
                int rps = (int)((double)globals.totallineswritten / elapsesec);                                     // calculate rows per second
                Console.WriteLine("Rows read from " + files.ToString() + " files: " + globals.totallinesread.ToString() + "  Rows written to SQL Table: " + globals.totallineswritten.ToString() + "  Total Time: " + elapsesec.ToString() + " seconds  Records/sec: " + rps.ToString());
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
            }
        }
    }
}
