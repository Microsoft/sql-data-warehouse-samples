/**********************************************************************************************************************************************************************************
*
*       Solution:   DBLoader
*       Module:     Globals
*       Copyright:  Copyright © 2018 Mitch van Huuksloot, Microsoft Corporation
*       Author:     Mitch van Huuksloot, Microsoft Corporation
*       Support:    This utility comes with no warranty of any kind, either express or implied. Neither Microsoft nor the author make any warranty of fitness for any purpose. 
*       Purpose:    Provides a class to gather all of the variables and configuration setting shared between all tasks. This module encapsulates reading of the values from the 
*                   configuration file and column metadata from SQL Server.
* 
***********************************************************************************************************************************************************************************/

using System;
using System.Data;
using System.Data.SqlClient;
using Microsoft.WindowsAzure.Storage;
using System.Configuration;

namespace DBLoader
{
    // class used to pass multiple return values back from the file processing Task back to the main thread
    class fileprocstats
    {
        public long linesread { get; set; }
        public long lineswritten { get; set; }
    }

    // class used to save column metadata from the target table for data type checking - single structure shared by all tasks
    class ColumnMetadata
    {
        public string Name;
        public string Type;
        public int Length;
        public bool Nullable;
        public bool stringtype = false;

        public ColumnMetadata(string name, string type, int length, bool nullable)
        {
            Name = name;
            Type = type;
            Length = length;
            Nullable = nullable;
            if (type.Contains("char")) stringtype = true;
        }
    }

    class globals
    {
        #region Class Constants (local)
        private const string sqlColMetaData1 = "select c.[name], t.[name], c.max_length, c.is_nullable " +
                                                "from sys.tables tb " +
                                                    "join sys.schemas s on (tb.schema_id= s.schema_id) " +
                                                    "join sys.columns c on (tb.object_id=c.object_id) " +
                                                    "join sys.types t on (c.user_type_id= t.user_type_id) " +
                                                "where tb.name = '";
        private const string sqlColMetaData2 = "' and s.name = '";
        private const string sqlColMetaData3 = "'";
        #endregion

        #region Class Static Variables (Globals)
        public static int buflen = 1048576;
        public static int threads = 8;
        public static string path = "", constr = "", nullstr = "";
        public static char delim, colsep, CRsub, LFsub;
        public static bool discardonerr = false, usestrdelim = true, fixdates = true;
        public static int maxbadcols = 999, rowsperbcwrite = 1000000;
        public static string schema = "dbo", tablename = "";
        public static ColumnMetadata[] Columns = new ColumnMetadata[1024];
        public static DataTable dt;
        public static long totallinesread = 0;
        public static long totallineswritten = 0;
        public static CloudStorageAccount csa;
        public static string tenantId;
        public static string applicationId;
        public static string secretKey;
        public static string adlsAccountName;
        #endregion

        public static void LoadConfigValues()
        {
            // read config variables from app.config
            string usd = ConfigurationManager.AppSettings.Get("UseStringDelimiter");
            if (usd.Substring(0, 1).ToUpper() == "Y" || usd.Substring(0, 1).ToUpper() == "T") usestrdelim = true;
            else usestrdelim = false;
            delim = Convert.ToChar(Convert.ToInt32(ConfigurationManager.AppSettings.Get("StringDelimiterDecimal")));
            colsep = Convert.ToChar(Convert.ToInt32(ConfigurationManager.AppSettings.Get("ColumnDelimiterDecimal")));
            CRsub = Convert.ToChar(Convert.ToInt32(ConfigurationManager.AppSettings.Get("CRSubstitutionDecimal")));
            LFsub = Convert.ToChar(Convert.ToInt32(ConfigurationManager.AppSettings.Get("LFSubstitutionDecimal")));
            nullstr = ConfigurationManager.AppSettings.Get("NullColumnValue").ToUpper();
            maxbadcols = Convert.ToChar(Convert.ToInt32(ConfigurationManager.AppSettings.Get("MaxBadColumnValues")));
            rowsperbcwrite = Convert.ToInt32(ConfigurationManager.AppSettings.Get("RowsPerTableWrite"));
            string doe = ConfigurationManager.AppSettings.Get("DiscardRowOnError");
            if (doe.Substring(0, 1).ToUpper() == "Y" || doe.Substring(0, 1).ToUpper() == "T") globals.discardonerr = true;
            else discardonerr = false;
            string fd = ConfigurationManager.AppSettings.Get("SilentlyReplaceInvalidDates");
            if (fd.Substring(0, 1).ToUpper() == "Y" || fd.Substring(0, 1).ToUpper() == "T") globals.fixdates = true;
            else fixdates = false;
            buflen = Convert.ToInt32(ConfigurationManager.AppSettings.Get("BufferSize"));
            threads = Convert.ToInt32(ConfigurationManager.AppSettings.Get("Threads"));
            try
            {
                csa = CloudStorageAccount.Parse(Microsoft.Azure.CloudConfigurationManager.GetSetting("StorageConnectionString"));
            }
            catch(Exception)
            {
                Console.WriteLine("Warning: exception loading storage connection - attempts to access files in Azure will fail.");
            }
            tenantId = ConfigurationManager.AppSettings.Get("TenantID");
            applicationId = ConfigurationManager.AppSettings.Get("ApplicationID");
            secretKey = ConfigurationManager.AppSettings.Get("SecretKey");
            adlsAccountName = ConfigurationManager.AppSettings.Get("ADLSAccount");
        }

        public static string GetMetadataSQL()
        {
            return "select * from " + schema + "." + tablename + " where 1=2";
        }

        public static string GetColumnMetadataSQL()
        {
            return sqlColMetaData1 + tablename + sqlColMetaData2 + schema + sqlColMetaData3;
        }

        public static bool LoadColumMetadata()
        {
            SqlConnection cn;
            SqlCommand cmd;
            try
            {
                globals.constr = System.Configuration.ConfigurationManager.AppSettings.Get("ConnectionString");
                cn = new SqlConnection(globals.constr);
                cn.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception connecting to specified database server: " + e.Message);
                return false;
            }
            try
            {
                // load datatable with column metadata
                string sql = globals.GetMetadataSQL();              // just schema
                cmd = new SqlCommand(sql, cn);
                cmd.CommandTimeout = 0;
                globals.dt = new DataTable(globals.tablename);
                globals.dt.Load(cmd.ExecuteReader());

                // fill a structure with actual SQL column metadata - DataTable metadata isn't helpful
                cmd.CommandText = globals.GetColumnMetadataSQL();
                SqlDataReader rdr = cmd.ExecuteReader(CommandBehavior.SingleResult);
                int row = 0;
                while (rdr.Read())
                {
                    ColumnMetadata cm = new ColumnMetadata(rdr.GetString(0), rdr.GetString(1), (int)rdr.GetInt16(2), rdr.GetBoolean(3));
                    globals.Columns[row] = cm;
                    row++;
                }
                rdr.Close();
                cmd.Connection = null;
                cn.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception loading table metadata: " + e.Message);
                return false;
            }
            return true;
        }
    }
}
