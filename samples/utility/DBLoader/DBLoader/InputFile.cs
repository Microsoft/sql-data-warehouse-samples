/**********************************************************************************************************************************************************************************
*
*       Solution:   DBLoader
*       Module:     InputFile
*       Copyright:  Copyright © 2018 Mitch van Huuksloot, Microsoft Corporation
*       Author:     Mitch van Huuksloot, Microsoft Corporation
*       Support:    This utility comes with no warranty of any kind, either express or implied. Neither Microsoft nor the author make any warranty of fitness for any purpose. 
*       Purpose:    A class to encapsulate all of the processing for a single file.
* 
***********************************************************************************************************************************************************************************/

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;

namespace DBLoader
{
    // class to manage multiple IO buffers and switching of buffers
    class IOBuffer
    {
        private char[][] buffer;
        private int curbuf = 0;

        public IOBuffer()
        {
            buffer = new char[2][];
            buffer[0] = new char[globals.buflen];                       // allocate actual IO buffers
            buffer[1] = new char[globals.buflen];
        }

        public char[] CurrentBuffer()
        {
            if (curbuf == 0) return buffer[0];
            else return buffer[1];
        }

        public char[] OtherBuffer()
        {
            if (curbuf == 0) return buffer[1];
            else return buffer[0];
        }

        public void Switch()
        {
            if (curbuf == 0) curbuf = 1;
            else curbuf = 0;
        }
    }

    class InputFile
    {
        #region Class Variables
        private DataTable dt, olddt;
        private DataRow dr;
        private string filename;
        private bool gzfile = true;
        private FileInfo fi = null;
        private Stream str = null;
        private int curcol = 0;
        public long filelines = 1, lines = 0, lineswritten = 0;
        private long maxbadcols = globals.maxbadcols;
        private int rowsperbcwrite = globals.rowsperbcwrite;
        private SqlBulkCopy bc = null;
        #endregion

        // Constructor - local file system file
        public InputFile(FileInfo fileinf)
        {
            fi = fileinf;
            filename = fi.Name;
            gzfile = (filename.ToUpper().EndsWith(".GZ") || filename.ToUpper().EndsWith(".GZIP") ? true : false);
        }

        // Constructor - Azure Blob Storage file
        public InputFile(Stream stream, string fname)
        {
            str = stream;
            filename = fname;
            gzfile = (filename.ToUpper().EndsWith(".GZ") || filename.ToUpper().EndsWith(".GZIP") ? true : false);
        }

        private bool OutputValueErrMsg(ref StringBuilder value, string exceptionmsg, string subvalue)
        {
            if (!(subvalue == null && globals.fixdates))                            // is flag set to ignore invalid dates? (usually too low a value like 0000/00/00, which doesn't work in SQL)
            {
                Console.WriteLine("File: " + filename + " Column value issue: Line: " + filelines.ToString() + " Row: " + (lines + 1).ToString() + " Column Name: " + globals.Columns[curcol].Name + " Type: " + globals.Columns[curcol].Type + " Length: " + globals.Columns[curcol].Length.ToString() +
                                    " Value Length: " + value.Length.ToString() + " Value: " + value.ToString().Substring(0, (value.Length > 25 ? 25 : value.Length)) + (value.Length > 25 ? "..." : "") +
                                    " - Exception: " + exceptionmsg);
            }

            if (!globals.discardonerr)
            {
                value.Clear();                                              // clear current value, since we won't be able to save it anyway
                if (subvalue == null)                                       // use passed in value for replacement, except for date types, which require additional handling
                {
                    if (globals.Columns[curcol].Type == "datetime2") value.Append("0001/01/01 00:00:00");
                    if (globals.Columns[curcol].Type == "datetime") value.Append("1753/01/01 00:00:00");
                    if (globals.Columns[curcol].Type == "smalldatetime") value.Append("1900/01/01 00:00:00");
                    if (globals.Columns[curcol].Type == "date") value.Append("0001/01/01");
                    if (globals.Columns[curcol].Type == "time") value.Append("00:00:00");
                    if (globals.fixdates) return true;                              // do not decrement bad column counter if config says to fix dates silently
                }
                else value.Append(subvalue);                                // replace value with passed in substitute 
                if (--maxbadcols == 0)                                      // check bad columns counter
                {
                    throw new Exception("\nExceeded maximum invalid column count");
                }
                return true;
            }
            else
            {
                if (--maxbadcols == 0)                                      // check bad columns counter
                {
                    throw new Exception("\nExceeded maximum invalid column count");
                }
                return false;
            }
        }

        private bool CheckColumnValue(ref StringBuilder value)
        {
            long curline = lines + 1;

            if (value.Length == 0 || value.ToString().ToUpper() == globals.nullstr)
            {
                if (globals.Columns[curcol].Nullable == true) return true;
                else return false;
            }
            switch (globals.Columns[curcol].Type)
            {
                case "char":
                case "nchar":
                case "varchar":
                case "nvarchar":
                    if (globals.Columns[curcol].Length != -1 && value.Length > globals.Columns[curcol].Length)
                    {
                        return OutputValueErrMsg(ref value, "String truncation", "\0x1A");
                    }
                    break;
                case "int":
                    try
                    {
                        SqlInt32 i = SqlInt32.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "-28");
                    }
                    break;
                case "bigint":
                    try
                    {
                        SqlInt64 i = SqlInt64.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "-28");
                    }
                    break;
                case "tinyint":
                    try
                    {
                        SqlByte i = SqlByte.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "28");
                    }
                    break;
                case "smallint":
                    try
                    {
                        SqlInt16 i = SqlInt16.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "-28");
                    }
                    break;
                case "numeric":
                case "decimal":
                    try
                    {
                        // SqlDecimal d = SqlDecimal.Parse(value.ToString());       // for very large values this may work, while .Net decimal overflows
                        decimal d = decimal.Parse(value.ToString());                // the DataTable uses .Net types, so overflows will cause exceptions in outer loop
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "-28.28");
                    }
                    break;
                case "float":
                case "real":
                    try
                    {
                        SqlSingle s = SqlSingle.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "-28.28");
                    }
                    break;

                case "datetime":
                case "datetime2":
                case "smalldatetime":
                    try
                    {
                        if (Convert.ToInt32(value.ToString().Substring(0, 4)) < (globals.Columns[curcol].Type == "datetime" ? 1753 : (globals.Columns[curcol].Type == "datetime2" ? 1 : 1900)))
                            return OutputValueErrMsg(ref value, "Year value is too small", null);
                        int dot = value.ToString().IndexOf(".");
                        if (dot > 0)                                                                    // fractional seconds given?
                        {
                            if (globals.Columns[curcol].Type == "smalldatetime")                        // strip fractional seconds off smalldatetime
                            {
                                string tmp = value.ToString();
                                value.Clear();
                                value.Append(tmp.Substring(0, tmp.IndexOf('.')));
                            }
                            else
                            {
                                if (value.Length - dot > 3)
                                {
                                    string tmp = value.ToString();
                                    value.Clear();
                                    value.Append(tmp.Substring(0, dot + 3));                            // limitation here is .NET datetime - in theory this could be a datetime2(1), which would fail...
                                }
                            }
                        }
                        SqlDateTime dt = SqlDateTime.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, null);
                    }
                    break;
                case "date":
                case "time":
                    try
                    {
                        DateTime d = DateTime.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, null);
                    }
                    break;
                case "uniqueidentifier":
                    try
                    {
                        SqlGuid u = SqlGuid.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "00000000-0000-0000-0000-000000000000");
                    }
                    break;
                case "bit":
                    try
                    {
                        SqlBoolean u = SqlBoolean.Parse(value.ToString());
                    }
                    catch (Exception e)
                    {
                        return OutputValueErrMsg(ref value, e.Message, "0");
                    }
                    break;
            }
            return true;
        }

        private async Task DoBulkCopy(DataTable thisdt)
        {
            // note that only one thread at a time does a bulk insert - so we don't have multi-threading issues accessing the bc object.
            int retry = 0, retrycon = 5;
            while (retry < 5)
            {
                try
                {
                    await bc.WriteToServerAsync(thisdt);                                                        // synchronously bulk write table to SQL table
                    break;
                }
                catch (Exception e)
                {
                    await Console.Out.WriteLineAsync("Exception on Bulk Copy Write: " + e.Message);
                    if (++retry >= 5) await Console.Out.WriteLineAsync("Error: Failed to perform bulk copy write to server.");
                    else
                    {
                        retrycon = 5;
                        bc.Close();                                                                             // close likely disconnected Bulk Copy object
                        await Console.Out.WriteLineAsync("Retry " + retry.ToString() + " of Bulk Copy Operation (after a 15 second wait)");
                        while (retrycon > 0)
                        {
                            Thread.Sleep(15000);
                            try
                            {
                                // re-initialize Bulk Copy object - connection etc.
                                bc = new SqlBulkCopy(globals.constr, SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.KeepIdentity) { DestinationTableName = "[" + globals.schema + "].[" + globals.tablename + "]" };
                                bc.BulkCopyTimeout = 0;
                                retrycon = 0;
                            }
                            catch (Exception e2)
                            {
                                await Console.Out.WriteLineAsync("Exception on reconnect of bulk copy connection: " + e2.Message);
                                await Console.Out.WriteLineAsync("Will retry connection in 15 seconds");
                                retrycon--;
                            }
                        }
                    }
                }
            }
            thisdt.Clear();                                                                                     // clear data table we were writing
            thisdt.Dispose();
        }


        public fileprocstats Process()
        {
            int ind, charrd;
            StringBuilder colval = new StringBuilder(8000);                                         // string buffer to build column value
            bool instr = false, escape = false, errorinrow = false;
            char prevchnewline = ' ';
            Task bctask = null;
            Task<int> rdtask = null;
            GZipStream gzfs = null;
            StreamReader sr = null;
            char[] buffer;

            try
            {
                IOBuffer iobuffer = new IOBuffer();                                                 // allocate buffers for file IO
                dt = globals.dt.Clone();                                                            // set up a DataTable with the table schema
                dr = dt.NewRow();                                                                   // get a new row with the schema structure
                bc = new SqlBulkCopy(globals.constr, SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.KeepIdentity) { DestinationTableName = "[" + globals.schema + "].[" + globals.tablename + "]" };
                bc.BulkCopyTimeout = 0;                                                             // never time out

                if (fi == null)                                                                     // file from Azure Blob Storage or ADL?
                {
                    if (gzfile)                                                                     // is the file a gzip?
                    {
                        gzfs = new GZipStream(str, CompressionMode.Decompress);                     // pull input stream through decompression stream
                        sr = new StreamReader(gzfs, true);                                          // stream through the stream reader we want
                    }
                    else sr = new StreamReader(str, true);                                          // not gzip, so just need a StreamReader
                }
                else
                {                                                                                   // local filesystem file
                    if (gzfile)                                                                     // is the file a gzip?
                    {
                        str = fi.OpenRead();                                                        // open stream from FileInfo
                        gzfs = new GZipStream(str, CompressionMode.Decompress);                     // decompression stream
                        sr = new StreamReader(gzfs, true);                                          // finally the stream reader we want
                    }
                    else sr = new StreamReader(fi.FullName, true);                                  // not gzip, so just need a StreamReader
                }

                while (true)                                                                        // main loop
                {
                    if (rdtask == null)                                                             // do we have a previous read task?
                    {
                        charrd = sr.ReadBlock(iobuffer.CurrentBuffer(), 0, globals.buflen);         // the first time we need to synchronously read
                        if (charrd == 0)                                                            // if we get nothing on the first read, this file must be empty
                        {
                            Console.WriteLine("File " + filename + " is empty.");                   // put out a warning and move on
                            break;
                        }
                    }
                    else
                    {
                        rdtask.Wait();                                                              // wait for any previous buffer read to complete
                        if (!rdtask.IsFaulted)                                                      // did the task have a fault (we assume it either completed or faulted, since we don't cancel tasks)
                        {
                            charrd = rdtask.Result;                                                 // grab the characters read from the completed async read block
                            iobuffer.Switch();                                                      // flip to the other buffer
                        }
                        else
                        {
                            foreach (var e in rdtask.Exception.InnerExceptions)                     // process any task exceptions
                            {
                                Console.WriteLine("Exception on read task of " + filename + ": " + e.Message);
                            }
                            break;
                        }
                        if (charrd == 0) break;                                                     // read and processed the whole file - so blow out of the outer while loop
                    }
                    rdtask = sr.ReadBlockAsync(iobuffer.OtherBuffer(), 0, globals.buflen);          // read the next part of the file into the other buffer while we are processing the current one
                    buffer = iobuffer.CurrentBuffer();                                              // simplify access to current character buffer for code below
                    for (ind = 0; ind < charrd; ind++)                                              // for each character in the current buffer;
                    {
                        if (buffer[ind] == '\\')                                                    // treat a backslash as an escape character
                        {
                            if (escape)                                                             // double escape is not required, so leave alone as two backslash characters
                            {
                                colval.Append("\\\\");                                              // append 2 backslashes
                                escape = false;                                                     // clear escape flag
                            }
                            else escape = true;                                                     // check for escaped string delimiter or escaped column separator
                            prevchnewline = ' ';                                                    // have a non newline character, so clear flag
                        }
                        else if (globals.usestrdelim && buffer[ind] == globals.delim)               // using string delimiter and this character is one (otherwise fall through and just append delimiter)
                        {
                            if (escape)                                                             // previous character an "escape"?
                            {
                                colval.Append(globals.delim);                                       // add just the string delimiter (remove escape) to the string value
                                escape = false;                                                     // turn off flag
                            }
                            else
                            {
                                if (instr) instr = false;                                           // end of delimited column
                                else instr = true;                                                  // in a delimited column
                            }
                            prevchnewline = ' ';                                                    // have a non newline character, so clear flag
                        }
                        else if (buffer[ind] == globals.colsep)                                     // character is a column separator character
                        {
                            if (escape || (globals.usestrdelim && instr))                           // if not using string delimiter then a column separator in a column value needs to be escaped to be part of the value
                            {
                                colval.Append(globals.colsep);                                      // append just the column separator character - removing the escape
                                escape = false;                                                     // turn off flag
                            }
                            else
                            {
                                if (colval.Length != 0 && globals.Columns[curcol].stringtype)       // if we have a column value (vs. an empty string, i.e. null) and it is a character type column
                                {                                                                   // check if user wants any CR/LF substitution
                                    if (globals.CRsub != 0 && colval.ToString().ToUpper() != globals.nullstr)    // no point in checking null values
                                        colval.Replace(globals.CRsub, '\r');                        // replace in column carriage return substitution characters with an actual CR
                                    if (globals.LFsub != 0 && colval.ToString().ToUpper() != globals.nullstr)
                                        colval.Replace(globals.LFsub, '\n');                        // replace in column line feed substitution characters with an actual LF
                                }
                                if (CheckColumnValue(ref colval))                                   // at the end of a column - process this column value by checking the value and saving it
                                {
                                    if (colval.Length == 0 || colval.ToString().ToUpper() == globals.nullstr) // zero length columns are treated as nulls and user can specify a string to treat as null
                                        dr.SetField<string>(curcol, null);                          // set null value in DataRow for this column
                                    else
                                        dr.SetField<string>(curcol, colval.ToString());             // set the current or corrected value for this column in the DataRow
                                }
                                else errorinrow = true;                                             // if the check of the value failed - set the error flag
                                colval.Clear();                                                     // set up for next column value
                                if (++curcol >= dt.Columns.Count)                                   // increment to the next column in the DataRow
                                    Console.WriteLine("File: " + filename + " Too many columns: Line: " + filelines.ToString() + " Row: " + (lines + 1).ToString());
                            }
                            prevchnewline = ' ';                                                    // have a non newline character, so clear flag
                        }
                        else if (buffer[ind] == '\n' || buffer[ind] == '\r')                        // have a newline character (either a carriage return or line feed)
                        {
                            if (prevchnewline == ' ' || buffer[ind] == prevchnewline)               // was last character a newline that we counted and not the same as the last one (i.e. 2 LFs = 2 lines)
                            {
                                filelines++;                                                        // increment actual line in file for debugging input values
                                prevchnewline = buffer[ind];                                        // save this character to compare to the next one
                            }
                            else prevchnewline = ' ';                                               // we only want to increment line once for CR+LF or LF+CR sequences (since the are treated as a single line end)
                            if (escape || (globals.usestrdelim && instr))                           // if string delimiter turned off, new line characters in column values need to be escaped
                            {
                                colval.Append(buffer[ind]);                                         // append newline character to column value
                                escape = false;
                            }
                            else if (colval.Length == 0 && curcol == 0) continue;                   // ignore blank lines (often at end of file) or second newline character
                            else
                            {                                                                       // otherwise we have the end of the line, so need to process the last column and the row
                                if (colval.Length != 0)                                             // if we have a column value (vs. an empty string, i.e. null)
                                {                                                                   // check if user wants any CR/LF substitution
                                    if (globals.CRsub != 0) colval.Replace(globals.CRsub, '\r');    // replace in column carriage return substitution characters with an actual CR
                                    if (globals.LFsub != 0) colval.Replace(globals.LFsub, '\n');    // replace in column line feed substitution characters with an actual LF
                                }
                                if (curcol >= dt.Columns.Count)                                     // too many columns?
                                    Console.WriteLine("File: " + filename + " Too many columns (" + curcol.ToString() + "): Line: " + filelines.ToString() + " Row: " + (lines + 1).ToString());
                                else
                                {
                                    if (curcol != dt.Columns.Count - 1)                             // warn on not enough columns
                                        Console.WriteLine("File: " + filename + " Too few columns (" + curcol.ToString() + "): Line: " + filelines.ToString() + " Row: " + (lines + 1).ToString());
                                    if (CheckColumnValue(ref colval))                               // at the end of a column - process this column value by checking the value and saving it
                                    {
                                        if (colval.Length == 0 || colval.ToString().ToUpper() == globals.nullstr) // zero length columns are treated as nulls as well as user specified string
                                            dr.SetField<string>(curcol, null);                      // set null value in DataRow for this column
                                        else
                                            dr.SetField<string>(curcol, colval.ToString());         // set the current or corrected value for this column in the DataRow
                                    }
                                    else errorinrow = true;                                         // if the check of the value failed - set the error flag
                                    if (!errorinrow)                                                // don't save the row if error (or we couldn't fix it transparently) and user has selected to ignore errors 
                                    {
                                        dt.Rows.Add(dr);                                            // add row to table
                                        lineswritten++;                                             // increment output line count
                                    }
                                }
                                colval.Clear();                                                     // set up for next row
                                errorinrow = false;                                                 // clear error flag for row
                                curcol = 0;                                                         // start on new line
                                lines++;                                                            // increment "line" count in the input file - i.e. the input row count
                                if (--rowsperbcwrite == 0)                                          // decrement rows per SQL write counter until we hit zero
                                {
                                    if (bctask != null) bctask.Wait();                              // did we have a previous async SQL bulk copy transfer? If so, wait for completion...
                                    olddt = dt;                                                     // save reference to current data table
                                    bctask = DoBulkCopy(olddt);                                     // asynchronously bulk write table to SQL table
                                    dt = globals.dt.Clone();                                        // clone table metadata only to reset DataTable
                                    rowsperbcwrite = globals.rowsperbcwrite;                        // reset count down counter for next SQL write
                                }
                                dr = dt.NewRow();                                                   // get a new row to play with in the new DataTable
                            }
                        }
                        else                                                                        // processing a "normal" character
                        {
                            if (escape)                                                             // was previous character an "escape"?
                            {
                                colval.Append('\\');                                                // was not an escape, just a backslash, so just append the character to the column value
                                escape = false;                                                     // turn off flag
                            }
                            colval.Append(buffer[ind]);                                             // append current character to column value
                            prevchnewline = ' ';                                                    // have a non newline character, so clear flag
                        }
                    }
                }
                if (curcol != 0 || colval.Length != 0)                                              // incomplete record at end of file?
                    Console.WriteLine("File: " + filename + " Too few columns (" + curcol.ToString() + "): Line: " + filelines.ToString() + " Row: " + (lines + 1).ToString() + " Incomplete value for Column Name: " + globals.Columns[curcol].Name + " - ROW NOT SAVED");
                if (bctask != null)                                                                 // did we have a previous async SQL buk copy transfer?
                {
                    bctask.Wait();                                                                  // wait for previous bulk copy to finish
                    olddt.Clear();                                                                  // clear data table we were writing
                    olddt.Dispose();
                }
                bc.WriteToServer(dt);                                                               // bulk write datatable to SQL Server
                bc.Close();
                dt.Clear();                                                                         // clean up table
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message + "\nFile: " + filename + " Line: " + filelines.ToString() + " Row: " + (lines + 1).ToString() + " Column: " + globals.Columns[curcol].Name + " Type: " + globals.Columns[curcol].Type + " Value Length: " + colval.Length.ToString() + " Column Length: " + globals.Columns[curcol].Length.ToString() + " Value: " + colval.ToString());
            }

            sr.Close();                                                                             // close stream reader
            if (gzfs != null) gzfs.Close();                                                         // close gz stream decompressor, if we 
            if (str != null) str.Close();                                                           // close the 

            fileprocstats ret = new fileprocstats();                                                // return multiple values from the worker thread using a simple class structure
            ret.linesread = lines;
            ret.lineswritten = lineswritten;
            return ret;
        }
    }
}
