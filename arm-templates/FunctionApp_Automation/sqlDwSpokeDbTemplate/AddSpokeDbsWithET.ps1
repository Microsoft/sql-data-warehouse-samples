﻿<# 
	This PowerShell script was automatically converted to PowerShell Workflow so it can be run as a runbook.
	Specific changes that have been made are marked with a comment starting with “Converter:”
#>
workflow spokeDbSetup {
	
		Param(	
			[Parameter(Mandatory= $true)]
			[String] $SqlServer,
			[Parameter(Mandatory= $true)]
			[String] $Datawarehouse,
			[Parameter(Mandatory= $true)]
			[String] $SpokeDbBaseName,
			[Parameter(Mandatory= $true)]
			[int] $SpokeCount
		)
	
	inlineScript {
	
	$logicalServerAdminCredential = Get-AutomationPSCredential -Name logicalServerAdminCredential 
	
	if ($logicalServerAdminCredential -eq $null) 
	{ 
	   throw "Could not retrieve '$logicalServerAdminCredential' credential asset. Check that you created this first in the Automation service." 
	}   
	# Get the username and password from the SQL Credential 
	$SqlUsername = $logicalServerAdminCredential.UserName 
	$SqlPass = $logicalServerAdminCredential.GetNetworkCredential().Password
	
	$SqlServer = $Using:SqlServer;
	$Datawarehouse = $Using:Datawarehouse;
	$SpokeDbBaseName = $Using:SpokeDbBaseName;
	$SpokeCount = $Using:SpokeCount;
	$SqlServerPort = '1433' 
	
	Write-Output $SqlServer
	Write-Output $Datawarehouse
	Write-Output $SpokeDbBaseName
	Write-Output $SpokeCount
	
	# Define the connection to the logical server master database 
	$MasterConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=master;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         
	# Open the SQL connection 
	$MasterConn.Open() 
	
	# Create logins for each database in master
	For ($i=0; $i -lt $SpokeCount; $i++) {
		$CreateDatabaseLoginInMaster=new-object system.Data.SqlClient.SqlCommand("
	CREATE LOGIN $SpokeDbBaseName$i WITH PASSWORD = 'p@ssw0rd##%$i';
	", $MasterConn)
		$CreateDatabaseLoginInMaster.CommandTimeout = 0;
		$CreateDatabaseLoginInMaster.ExecuteNonQuery()

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($CreateDatabaseLoginInMaster) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
	}
	$MasterConn.Close() 
	
	
	# Define the connection to the SQL data warehouse instance
	$DwConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=$Datawarehouse;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         
	# Open the SQL connection 
	$DwConn.Open() 
	
	# Create user for each database in the data warehouse instance and setup meta schema
	For ($i=0; $i -lt $SpokeCount; $i++) {
		$CreateDatabaseUserInDw=new-object system.Data.SqlClient.SqlCommand("
		CREATE USER $SpokeDbBaseName$i FOR LOGIN $SpokeDbBaseName$i;

		IF NOT EXISTS (SELECT * FROM sys.schemas sch WHERE sch.[name] = 'meta')
		BEGIN
		EXEC sp_executesql N'CREATE SCHEMA [meta]'
		END
		", $DwConn)
		$CreateDatabaseUserInDw.CommandTimeout=0
		$CreateDatabaseUserInDw.ExecuteNonQuery()
	
	#    $Da=New-Object system.Data.SqlClient.SqlDataAdapter($CreateDatabaseUserInDw) 
	#    $Ds=New-Object system.Data.DataSet 
	#    [void]$Da.fill($Ds)
	   
	}
	$DwConn.Close() 
	
	
	# Setup each database instance with connections to the data warehouse instance given the credentials just created and setup meta schema
	For ($i=0; $i -lt $SpokeCount; $i++) {
		$DbConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=$SpokeDbBaseName$i;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         

		$DbConn.Open() 
		$SetupDatabaseEQCredentials=new-object system.Data.SqlClient.SqlCommand("
		IF NOT EXISTS (SELECT * FROM sys.symmetric_keys WHERE symmetric_key_id = 101)
		CREATE MASTER KEY;


		CREATE DATABASE SCOPED CREDENTIAL [$Datawarehouse-Credential]
		WITH IDENTITY = '$SpokeDbBaseName$i',
		SECRET = 'p@ssw0rd##%$i';


		CREATE EXTERNAL DATA SOURCE [$Datawarehouse] WITH 
		(TYPE = RDBMS, 
		LOCATION = '$SqlServer.database.windows.net', 
		DATABASE_NAME = '$Datawarehouse', 
		CREDENTIAL = [$Datawarehouse-Credential], 
		);

		IF NOT EXISTS (SELECT * FROM sys.schemas sch WHERE sch.[name] = 'meta')
		BEGIN
		EXEC sp_executesql N'CREATE SCHEMA [meta]'
		END
		", $DbConn)
		$SetupDatabaseEQCredentials.CommandTimeout=0
		$SetupDatabaseEQCredentials.ExecuteNonQuery()
		

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($SetupDatabaseEQCredentials) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
		$DbConn.Close() 
	}
	
	############## Load DW with stored procedures ##############
	# Define the connection to the SQL data warehouse instance
	$DwConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=$Datawarehouse;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         
	# Open the SQL connection 
	$DwConn.Open() 
	
		# Create user for each database in the data warehouse instance
		$CreateExternalTableFromDw=new-object system.Data.SqlClient.SqlCommand("
		CREATE PROC [meta].[CreateExternalTableFromDw] @externalSchema [VARCHAR](50),@schemaName [VARCHAR](50),@tableName [VARCHAR](255),@nameAppendix [VARCHAR](255),@externalDataSource [VARCHAR](255),@sqlCmd [VARCHAR](8000) OUT AS
		BEGIN
		DECLARE @distributionType AS VARCHAR(50);
		DECLARE @distributionColumn AS VARCHAR(255);
		DECLARE @indexType AS VARCHAR(50);
		DECLARE @createClause AS VARCHAR(1000);
		DECLARE @columnOrdinal AS INT;
		DECLARE @columnDefinition AS VARCHAR(255);
		DECLARE @columnList AS VARCHAR(8000);
		DECLARE @distributionClause AS VARCHAR(1000);
		DECLARE @indexClause AS VARCHAR(1000);

		--> Construct the 'CREATE TABLE ...' clause
		SET @createClause = 'CREATE EXTERNAL TABLE [' + @externalSchema + '].[' + @nameAppendix + '_' +@tableName + ']';

		--> Construct the column list
		SET @columnList = '(' + CHAR(13)+CHAR(10) + '   ';
		SET @columnDefinition = '';
		SET @columnOrdinal = 0;

		WHILE @columnDefinition IS NOT NULL
		BEGIN
		IF @columnOrdinal > 1
		SET @columnList = @columnList + ',' + CHAR(13)+CHAR(10) + '   ';

		IF @columnOrdinal > 0
		SET @columnList = @columnList + @columnDefinition;

		SET @columnOrdinal = @columnOrdinal + 1;

		SET @columnDefinition = (SELECT '[' + [COLUMN_NAME] + '] [' + [DATA_TYPE] + ']' 
					+ CASE WHEN [DATA_TYPE] LIKE '%char%' THEN CASE WHEN [CHARACTER_MAXIMUM_LENGTH] = -1 THEN '(MAX)' ELSE ISNULL('(' + CAST([CHARACTER_MAXIMUM_LENGTH] AS VARCHAR(10)) + ')','') END ELSE '' END
					+ CASE WHEN [DATA_TYPE] LIKE '%binary%' THEN CASE WHEN [CHARACTER_MAXIMUM_LENGTH] = -1 THEN '(MAX)' ELSE ISNULL('(' + CAST([CHARACTER_MAXIMUM_LENGTH] AS VARCHAR(10)) + ')','') END ELSE '' END
					+ CASE WHEN [DATA_TYPE] LIKE '%decimal%' THEN ISNULL('(' + CAST([NUMERIC_PRECISION] AS VARCHAR(10)) + ', ' + CAST([NUMERIC_SCALE] AS VARCHAR(10)) + ')','') ELSE '' END
					+ CASE WHEN [DATA_TYPE] LIKE '%numeric%' THEN ISNULL('(' + CAST([NUMERIC_PRECISION] AS VARCHAR(10)) + ', ' + CAST([NUMERIC_SCALE] AS VARCHAR(10)) + ')','') ELSE '' END
					+ CASE WHEN [DATA_TYPE] in ('datetime2','datetimeoffset') THEN ISNULL('(' + CAST([DATETIME_PRECISION] AS VARCHAR(10)) + ')','') ELSE '' END
					+ CASE WHEN [IS_NULLABLE] = 'YES' THEN ' NULL' ELSE ' NOT NULL' END
					FROM INFORMATION_SCHEMA.COLUMNS
					WHERE [TABLE_SCHEMA] = @schemaName
					AND [TABLE_NAME] = @tableName
					AND [ORDINAL_POSITION] = @columnOrdinal);
		END
		SET @columnList = @columnList +  + CHAR(13)+CHAR(10) + ')';     

		--> Construct the entire sql command by combining the individual clauses
		SET @sqlCmd = @createClause
			+ ' ' + @columnList
			+ ' WITH ('  + CHAR(13)+CHAR(10) +'DATA_SOURCE = ' + @externalDataSource
			+ ', ' + CHAR(13)+CHAR(10) +'SCHEMA_NAME  = N' + '''' +  @schemaName + ''''
			+ ', ' + CHAR(13)+CHAR(10) +'OBJECT_NAME  = N' + '''' + @tableName + ''''
			+ CHAR(13)+CHAR(10) + ')'
			;
		END

		", $DwConn)
		$CreateExternalTableFromDw.CommandTimeout = 0
		$CreateExternalTableFromDw.ExecuteNonQuery()

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($CreateExternalTableFromDw) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
	
	
		$GenerateDatamartExternalTableDefinitionsAndGrantSelect=new-object system.Data.SqlClient.SqlCommand("
		CREATE PROC [meta].[GenerateDatamartExternalTableDefinitionsAndGrantSelect] AS
		BEGIN
			IF EXISTS ( SELECT * FROM sys.tables WHERE object_id = OBJECT_ID('meta.DatamartExternalTableDefinitions') )
			BEGIN
				DROP TABLE meta.DatamartExternalTableDefinitions
			END
			
			-- Gets distinct schema table combinations in control table
			CREATE TABLE meta.DatamartExternalTableDefinitions 
			WITH (DISTRIBUTION=ROUND_ROBIN, HEAP)
			AS
			SELECT	ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS [Sequence]
			,		[TableName] 
			,		[SchemaName]
			,		CAST('' AS NVARCHAR(MAX)) AS [DDL]
			FROM [meta].[DatamartControlTable]
			GROUP BY	[TableName] 
			,			[SchemaName]	
		
			DECLARE @databaseName VARCHAR(100) = DB_NAME();
		
			-- Generates DDL statements for each schema/table in control table
			DECLARE @nbr_statements INT = (SELECT COUNT(*) FROM meta.DatamartExternalTableDefinitions)
			,       @i INT = 1
			;
			
			WHILE   @i <= @nbr_statements
			BEGIN
				DECLARE @DDL NVARCHAR(MAX); 
				DECLARE @tableName	VARCHAR(1000)				= (SELECT [TableName]		FROM meta.DatamartExternalTableDefinitions WHERE Sequence = @i);
				DECLARE @schemaName	VARCHAR(1000)				= (SELECT [SchemaName]		FROM meta.DatamartExternalTableDefinitions WHERE Sequence = @i);
				EXEC    [meta].[createExternalTableFromDw] @databaseName, @schemaName, @tableName, @schemaName, @databaseName, @DDL OUTPUT;
				UPDATE  meta.DatamartExternalTableDefinitions SET DDL = @DDL WHERE Sequence = @i;
		
				SET     @i +=1;
			END
		
			-- Creates RemoteTableView for data marts to get their DDL statements
			IF NOT EXISTS (SELECT * FROM sys.objects obj WHERE obj.[name] = 'RemoteTableDefinitionView' and obj.[type] = 'V')
				EXEC sp_executesql N'
				CREATE VIEW [meta].[RemoteTableDefinitionView] AS 
				SELECT	ct.[TableName]
				,		ct.[SchemaName]
				,		et.[DDL]
				FROM	[meta].[DatamartExternalTableDefinitions] et
				JOIN	[meta].[DatamartControlTable] ct
				ON		ct.[TableName] = et.[TableName] AND ct.[SchemaName] = et.[SchemaName]
				WHERE	ct.[DataMartUser] = SUSER_SNAME();'
		
			-- Grants SELECT Permissions to all data mart users for control table permissions plus the remote table view
			CREATE TABLE #GrantPermissionsFromControlTable
			WITH (DISTRIBUTION=ROUND_ROBIN, HEAP)
			AS 
			SELECT	ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS [Sequence]
			,		ct.[DataMartUser]
			,		ct.[DataSource]
			,		ct.[TableName]
			,		ct.[SchemaName]
			FROM	[meta].[DatamartControlTable] ct
		
			DECLARE @nbr_statements2 INT = (SELECT COUNT(*) FROM #GrantPermissionsFromControlTable)
			,       @j INT = 1
			;
			
			WHILE   @j <= @nbr_statements
			BEGIN
				DECLARE @tableName2		VARCHAR(1000)			= (SELECT [TableName]		FROM #GrantPermissionsFromControlTable WHERE Sequence = @j);
				DECLARE @schemaName2	VARCHAR(1000)			= (SELECT [SchemaName]		FROM #GrantPermissionsFromControlTable WHERE Sequence = @j);
				DECLARE @datamartUser VARCHAR(1000)				= (SELECT [DataMartUser]	FROM #GrantPermissionsFromControlTable WHERE Sequence = @j);
				DECLARE @grantCommand NVARCHAR(100)				= 'GRANT SELECT ON OBJECT::['+@schemaName2+'].['+@tableName2+'] TO ['+@datamartUser+'];';
				DECLARE @grantViewCommand NVARCHAR(100)			= 'GRANT SELECT ON OBJECT::[meta].[RemoteTableDefinitionView] TO '+@datamartUser+';';
				EXEC sp_executesql @grantCommand;
				EXEC sp_executesql @grantViewCommand;
				SET     @j +=1;
			END
		
			DROP TABLE #GrantPermissionsFromControlTable
		
		END
		", $DwConn)
		$GenerateDatamartExternalTableDefinitionsAndGrantSelect.CommandTimeout = 0
		$GenerateDatamartExternalTableDefinitionsAndGrantSelect.ExecuteNonQuery()

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($GenerateDatamartExternalTableDefinitionsAndGrantSelect) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)

	
		$AddObjectsForDatamartUserToControlTable=new-object system.Data.SqlClient.SqlCommand("
		CREATE PROC [meta].[AddObjectsForDatamartUserToControlTable] @userName [VARCHAR](150),@dataSource [VARCHAR](150),@objectId [VARCHAR](150),@schemaName [VARCHAR](150) AS
		BEGIN
		-- SET NOCOUNT ON added to prevent extra result sets from
		-- interfering with SELECT statements.
		SET NOCOUNT ON

		-- Insert statements for procedure here

		IF OBJECT_ID('tempdb..#TablesByUserSchema') IS NOT NULL DROP TABLE #TablesByUserSchema


		IF @objectId IS NOT NULL OR @schemaName IS NOT NULL
		BEGIN
			-- If objectId is set, just add the unique object to datamart user. ObjectId always
			-- takes precedence over schema
			IF (@objectId IS NOT NULL)
			BEGIN
					SELECT  @userName  AS [DataMartUser]
					,   @dataSource  AS [DataSource]
					,   tbl.[object_id] AS [ObjectId] 
					,   sch.[name]  AS [SchemaName]
					,   tbl.[name]  AS [TableName] 
					INTO  #TablesByUserSchema
					FROM  sys.tables tbl
					JOIN  sys.schemas sch  ON tbl.[schema_id] = sch.[schema_id]
					WHERE  tbl.[object_id]  = @objectId
					AND	tbl.[is_external] = 0
					AND	sch.[name] != 'meta' 
			END
			-- If schemaName is set, add all tables in the schema to datamart user
			-- but not objectId
			ELSE 
			BEGIN
					SELECT 
					@userName  AS [DataMartUser]
					, @dataSource  AS [DataSource]
					, tbl.[object_id] AS [ObjectId] 
					, sch.[name]  AS [SchemaName]
					, tbl.[name]  AS [TableName] 
					INTO #TablesByUserSchema
					FROM sys.tables tbl
					JOIN sys.schemas sch
					ON tbl.[schema_id] = sch.[schema_id]
					WHERE	sch.[name] = @schemaName
					AND 	tbl.[is_external] = 0
					AND	sch.[name] != 'meta' 
			END
		END
		-- If no optional parameters given, add all user tables to datamart user
		ELSE
		BEGIN
			SELECT 
				@userName  AS [DataMartUser]
			, @dataSource  AS [DataSource]
			, tbl.[object_id] AS [ObjectId] 
			, sch.[name]  AS [SchemaName]
			, tbl.[name]  AS [TableName] 
			INTO #TablesByUserSchema
			FROM	sys.tables tbl
			JOIN	sys.schemas sch
			ON tbl.[schema_id] = sch.[schema_id]
			AND tbl.[is_external] = 0
			AND	sch.[name] != 'meta' 
		END

		IF NOT EXISTS ( SELECT * FROM sys.tables WHERE object_id = OBJECT_ID('meta.DatamartControlTable') )
		BEGIN
			CREATE TABLE meta.[DatamartControlTable]
			WITH
			(
				HEAP,
				DISTRIBUTION=ROUND_ROBIN
			)
			AS SELECT * FROM #TablesByUserSchema
		END
		ELSE
		BEGIN
			CREATE TABLE meta.[DatamartControlTable_new]
			WITH
			(
				HEAP
			, DISTRIBUTION=ROUND_ROBIN
			)
			AS
			SELECT
				*
			FROM #TablesByUserSchema t
			WHERE NOT EXISTS
			(
				SELECT * 
				FROM meta.[DatamartControlTable] c 
				WHERE t.[DataMartUser] = c.[DataMartUser] AND t.[ObjectId] = c.[ObjectId]
			)
			UNION ALL
			SELECT * FROM meta.[DatamartControlTable]

			RENAME OBJECT meta.[DatamartControlTable]  TO [DatamartControlTable_old];
			RENAME OBJECT meta.[DatamartControlTable_new] TO [DatamartControlTable];

			DROP TABLE [meta].[DatamartControlTable_old];
			DROP TABLE #TablesByUserSchema;

		END

		END
		", $DwConn)
		$AddObjectsForDatamartUserToControlTable.CommandTimeout=0
		$AddObjectsForDatamartUserToControlTable.ExecuteNonQuery()

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($AddObjectsForDatamartUserToControlTable) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
		
	
	# $RemoteTableDefinitionView=new-object system.Data.SqlClient.SqlCommand("
	# 	CREATE VIEW [meta].[RemoteTableDefinitionView] AS 
	# 	SELECT	[TableName]
	# 	,		[SchemaName]
	# 	,		[DDL]
	# 	FROM [meta].[DatamartExternalTableDefinitions]
	# 	WHERE DataMartUser = SUSER_SNAME();
	# ", $DwConn)
	
	# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($RemoteTableDefinitionView) 
	# $Ds=New-Object system.Data.DataSet 
	# [void]$Da.fill($Ds)
	
	
	$DwConn.Close() 
	
	
	############## Load each database with stored procedures ##############

	# Setup each database instance with connections to the data warehouse instance given the credentials just created
			
	For ($i=0; $i -lt $SpokeCount; $i++) {
		$DbConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=$SpokeDbBaseName$i;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         
		
		$DbConn.Open() 
		$CreateMetaSchemaDb=new-object system.Data.SqlClient.SqlCommand("
		IF NOT EXISTS (SELECT * FROM sys.schemas sch WHERE sch.[name] = 'meta')
		BEGIN
		EXEC sp_executesql N'CREATE SCHEMA [meta]'
		END", $DbConn)
		$CreateMetaSchemaDb.CommandTimeout = 0
		$CreateMetaSchemaDb.ExecuteNonQuery()

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($CreateMetaSchemaDb) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
		
		$SetupExternalTablesToDw=new-object system.Data.SqlClient.SqlCommand("
		
		CREATE PROC [meta].[SetupExternalTablesToDw] @externalTableSource VARCHAR(100) AS
		BEGIN
			IF NOT EXISTS (SELECT * FROM sys.schemas sch WHERE sch.[name] = @externalTableSource)
			BEGIN
				DECLARE @createSchemaCmd NVARCHAR(100) = N'CREATE SCHEMA [' + @externalTableSource + ']';
				EXEC sp_executesql @createSchemaCmd;
			END
		 
			IF NOT EXISTS ( SELECT * 
				FROM sys.external_tables et
				JOIN sys.schemas sch
				ON  et.[schema_id] = sch.[schema_id]
				AND  et.[name] = 'RemoteTableDefinitionView' )
			BEGIN
				DECLARE @createRemoteTableDefinitionViewCmd NVARCHAR(400) = 
				'
				CREATE EXTERNAL TABLE [meta].[RemoteTableDefinitionView]
				(
					[TableName]  NVARCHAR(128) NOT NULL
				,	[SchemaName] NVARCHAR(128) NOT NULL
				,	[DDL]   NVARCHAR(MAX)  NULL
				)
				WITH
				(
					DATA_SOURCE = '+@externalTableSource+'
				,	SCHEMA_NAME = ''meta''
				,	OBJECT_NAME = ''RemoteTableDefinitionView''
				)
				'
				EXEC sp_executesql @createRemoteTableDefinitionViewCmd;
			END
		
			SELECT ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS Sequence
			,    [TableName] 
			,    [SchemaName]
			,    [DDL]  
			INTO #RemoteTableDefinitions
			FROM [meta].[RemoteTableDefinitionView]
		
			DECLARE @nbr_statements INT = (SELECT COUNT(*) FROM #RemoteTableDefinitions)
			,       @i INT = 1
			;
		
			WHILE   @i <= @nbr_statements
			BEGIN
				DECLARE @cmd NVARCHAR(MAX)			= (SELECT [DDL]				FROM #RemoteTableDefinitions WHERE Sequence = @i); 
				DECLARE @tableName	VARCHAR(1000)	= (SELECT [TableName]		FROM #RemoteTableDefinitions WHERE Sequence = @i);
				DECLARE @schemaName	VARCHAR(1000)	= (SELECT [SchemaName]		FROM #RemoteTableDefinitions WHERE Sequence = @i);	
		
				IF EXISTS (  SELECT * 
				FROM sys.external_tables et
				JOIN sys.schemas sch
				ON  et.[schema_id] = sch.[schema_id]
				WHERE	et.[name]	= @tableName
				AND		sch.[name]	= @schemaName )
				BEGIN
					DECLARE @dropExternalTableCmd NVARCHAR(MAX) = 'DROP EXTERNAL TABLE '+ @schemaName + '.[' + @tableName + ']' 
					EXEC sp_executesql @dropExternalTableCmd
				END
		
				EXEC sp_executesql @cmd
				SET     @i +=1;
			END
		
			DROP TABLE #RemoteTableDefinitions
		END
		", $DbConn)
		$SetupExternalTablesToDw.CommandTimeout=0
		$SetupExternalTablesToDw.ExecuteNonQuery()
		
		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($SetupExternalTablesToDw) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
		# $DbConn.Close() 
	}
		
	
	### Execute stored procedures to generate the control tables and external metadata information for each of the dbs created against the DW
	
	$DwConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=$Datawarehouse;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         
	# Open the SQL connection 
	$DwConn.Open() 
	
	For ($i=0; $i -lt $SpokeCount; $i++) {
		$AddObjectsForDatamartUserToControlTable=new-object system.Data.SqlClient.SqlCommand("
	EXEC [meta].[AddObjectsForDatamartUserToControlTable] '$SpokeDbBaseName$i', '$Datawarehouse', null, null
	", $DwConn)
		$AddObjectsForDatamartUserToControlTable.CommandTimeout=0
		$AddObjectsForDatamartUserToControlTable.ExecuteNonQuery()
		
		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($AddObjectsForDatamartUserToControlTable) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
	}
	
	$GenerateDatamartExternalTableDefinitionsAndGrantSelect=new-object system.Data.SqlClient.SqlCommand("
	EXEC [meta].[GenerateDatamartExternalTableDefinitionsAndGrantSelect]
	", $DwConn)
	$GenerateDatamartExternalTableDefinitionsAndGrantSelect.CommandTimeout=0
	$GenerateDatamartExternalTableDefinitionsAndGrantSelect.ExecuteNonQuery()
	# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($GenerateDatamartExternalTableDefinitionsAndGrantSelect) 
	# $Ds=New-Object system.Data.DataSet 
	# [void]$Da.fill($Ds)
	
	
	$DwConn.Close() 

	Start-Sleep -s 60
	
	### Execute stored procedures to generate external table definitions in each of the databases
	
	For ($i=0; $i -lt $SpokeCount; $i++) {
		$DbConn = New-Object System.Data.SqlClient.SqlConnection("Server=tcp:$SqlServer.database.windows.net,$SqlServerPort;Initial Catalog=$SpokeDbBaseName$i;Persist Security Info=False;User ID=$SqlUsername;Password=$SqlPass;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=0;")         
	
		$DbConn.Open() 
		$SetupDatabaseEQCredentials=new-object system.Data.SqlClient.SqlCommand("
	EXEC [meta].[SetupExternalTablesToDw] '$Datawarehouse'
	", $DbConn)
		$SetupDatabaseEQCredentials.CommandTimeout=0
		$SetupDatabaseEQCredentials.ExecuteNonQuery()

		# $Da=New-Object system.Data.SqlClient.SqlDataAdapter($SetupDatabaseEQCredentials) 
		# $Ds=New-Object system.Data.DataSet 
		# [void]$Da.fill($Ds)
		$DbConn.Close() 
	}
	
	
	}
}