using dtSearch.Engine;
using DtSearchIndexer.IndexerUtility.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DtSearchIndexer.IndexerUtility.DataSources {
  public class DbDataSource : DataSource {
    public DbDataSource(DbInfo dbInfo, int recsPerRetrieval = 150_000) {
      this.dbInfos = new List<DbInfo>() { dbInfo };
      recordsPerRetrieval = recsPerRetrieval;
    }

    public DbDataSource(List<DbInfo> dbInfos, int recsPerRetrieval = 150_000) {
      this.dbInfos = dbInfos;
      recordsPerRetrieval = recsPerRetrieval;
    }

    private readonly List<DbInfo> dbInfos;
    private readonly int recordsPerRetrieval;

    private int dbInfoIndex;
    private int dbInfoEntityIndex;
    private int dbInfoRowOffset;

    private DataSet dataSet;
    private int curTableIndex;
    private int curRowIndex;

    private bool bDatabaseFailed;
    public string ErrorMessage { get; private set; }
    public bool WasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string BaseImageDir { get; set; }
    public bool IsTest { get; set; }
    public string BaseImageDir_Test { get; set; }

    // TODO: Track the keyCols for the records that were indexed
    //     - This should include the Context and TableName
    public List<IndexedItem> IndexedItems { get; set; } = new List<IndexedItem>();

    #region From Interface
    public string DocName { get; set; }
    public bool DocIsFile { get; set; }
    public string DocDisplayName { get; set; }
    public DateTime DocModifiedDate { get; set; }
    public DateTime DocCreatedDate { get; set; }
    public string DocText { get; set; }
    public string DocFields { get; set; }
    public int DocId { get; set; }
    public int DocWordCount { get; set; }
    public TypeId DocTypeId { get; set; }
    public bool WasDocError { get; set; }
    public string DocError { get; set; }
    public Stream DocStream { get; set; }
    public byte[] DocBytes { get; set; }
    public bool HaveDocBytes { get; private set; }
    public string IndexPath { get; set; }

    public bool GetNextDoc() {
      if (bDatabaseFailed) {
        return false;
      }

      if (dataSet is null) {
        if (!Rewind() || bDatabaseFailed) {
          return false;
        }
      }

      getMoreRecords:
      if (curTableIndex >= dataSet.Tables.Count || curRowIndex >= dataSet.Tables[curTableIndex].Rows.Count) {
        if (!GetMoreRecords() || bDatabaseFailed || dataSet is null || (dataSet.Tables?.Count ?? 0) == 0) {
          return false;
        }
        curTableIndex = 0;
        curRowIndex = 0;
      }

      while (dataSet.Tables[curTableIndex].Rows.Count == 0) {
        curTableIndex++;
        if (curTableIndex >= dataSet.Tables.Count) {
          if (dbInfoIndex >= dbInfos.Count) { return false; }
          goto getMoreRecords;
        }
      }

      var curTable = dataSet.Tables[curTableIndex];
      var curRow = curTable.Rows[curRowIndex];
      var keyCols = new List<string>() { $"Id={(curRow[0] is DateTime ? DateToString((DateTime)curRow[0]) : curRow[0])}" };

      if (dbInfos is { }) {
        var tableInfo = dbInfos
          .Select(dbi => new {
            DbName = $"{dbi.DbName}".Trim(),
            DbContext = dbi.DbContext,
            Tables = dbi.Tables.Select(t => new DbTableInfo() {
              TableName = $"[{dbi.DbName}.{t.TableName}]",
              FieldsToIndex = t.FieldsToIndex,
              KeyColumns = t.KeyColumns
            })
          })
          .Single(dbi => dbi.Tables.Any(t => t.TableName == curTable.TableName));

        keyCols = tableInfo
          .Tables.Single(x => x.TableName == curTable.TableName)
          .KeyColumns
          .Select(k => $"{curTable.Columns[k].ColumnName}={(curRow[k] is DateTime ? DateToString((DateTime)curRow[k]) : curRow[k])}".Trim()).ToList();
      }

      DocModifiedDate = DateTime.Now;
      DocCreatedDate = DateTime.Now;
      DocBytes = null;
      HaveDocBytes = false;
      DocText = "";
      DocFields = "";
      DocIsFile = false;
      DocTypeId = TypeId.it_DatabaseRecord;

      DocName = $"db://{curTable.TableName}#{string.Join("|", keyCols)}";
      DocDisplayName = $"{curTable.TableName.Split('.').First().Substring(1)}|[DB]|#{string.Join("&", keyCols)}";
      //DocId = GetDocId(DocName);
      for (var i = 0; i < curTable.Columns.Count; i++) {
        var colName = curTable.Columns[i].ColumnName;
        var colVal = (curRow[i] is DateTime ? DateToString((DateTime)curRow[i]) : curRow[i].ToString()).Replace("\t", " ").Trim();
        DocFields += $"{colName}\t{colVal}\t";
        if (colName.Equals("Date_Entered") && DateTime.TryParse(colVal, out DateTime createdDate)) {
          DocCreatedDate = createdDate.ToLocalTime();
        } else if (colName.Equals("DB")) {
          DocDisplayName = DocDisplayName.Replace("[DB]", colVal.Trim());
        } else if (colName.Equals("DocID")) {
          IndexedItems.Add(new IndexedItem() {
            IndexName = dbInfos.Single(dbi => dbi.Tables.Any(t => t.TableName == curTable.TableName.Split('.').Last().Replace("]", ""))).IndexPath.Split(new[] { '\\', '/' }).Last(),
            DocID = int.Parse(colVal)
          });
        } else
        // Obtain the binary contents of a document to be indexed
        // as part of this row, and pass it in DocBytes to the indexer.
        if (colName.Equals("Image_Loc")) {
          try {
            string filename = colVal;
            if (!Path.IsPathRooted(filename)) {
              filename = Path.Combine(BaseImageDir, filename);
              if (IsTest && !File.Exists(filename)) {
                filename = Path.Combine(BaseImageDir_Test, filename);
              }
            }
            if (File.Exists(filename)) {
              FileStream reader = File.OpenRead(filename);
              if (reader.Length > 0) {
                byte[] fileData = new byte[reader.Length];
                reader.Read(fileData, 0, (int)reader.Length);
                DocBytes = fileData;
                HaveDocBytes = true;
              }
            }
          } catch (Exception ex) {
            Console.Error.WriteLine($"The file index portion for {DocName} failed: {ex.Message}");
            Console.Error.WriteLine(ex);
            DocBytes = null;
            HaveDocBytes = false;
          }
        }
      }

      curRowIndex++;

      // If we are at the last row in the current table and the current table is not the last table in the DataSet
      if (curRowIndex >= curTable.Rows.Count && curTableIndex < dataSet.Tables.Count - 1) {
        curTableIndex++;
        curRowIndex = 0;
      }

      return true;
    }

    public async Task<string> CreateRemoveListForUpdateRequest(List<string> manualDocList = null) {
      var indexTables = new List<string>();
      dbInfos.ForEach(dbi => {
        indexTables.AddRange(dbi.Tables.Select(t => $"[{dbi.DbName}.{t.TableName}]").ToList());
      });
      indexTables = indexTables.Distinct().ToList();
      var docNamesToDelete = manualDocList?.Where(dl => indexTables.Any(it => dl.Contains(it)))?.ToList() ?? new List<string>();
      while (GetMoreRecords() && !bDatabaseFailed) {
        for (var i = 0; i < dataSet.Tables.Count; i++) {
          var curTable = dataSet.Tables[i];
          for (var j = 0; j < curTable.Rows.Count; j++) {
            var curRow = curTable.Rows[curRowIndex];
            var keyCols = dbInfos
                .Select(dbi => new {
                  DbName = $"{dbi.DbName}".Trim(),
                  DbContext = dbi.DbContext,
                  Tables = dbi.Tables.Select(t => new DbTableInfo() {
                    TableName = $"[{dbi.DbName}.{t.TableName}]",
                    FieldsToIndex = t.FieldsToIndex,
                    KeyColumns = t.KeyColumns
                  })
                })
                .Single(dbi => dbi.Tables.Any(t => t.TableName == curTable.TableName))
                .Tables.Single(x => x.TableName == curTable.TableName)
                .KeyColumns
                .Select(k => $"{curTable.Columns[k].ColumnName}={(curRow[k] is DateTime ? DateToString((DateTime)curRow[k]) : curRow[k])}".Trim()).ToList();

            docNamesToDelete.Add($"db://{curTable.TableName}#{string.Join("|", keyCols)}");
          }
        }
      }

      // Write the files to disk
      var deleteFileName = Path.Combine(BaseImageDir, "DeleteList.txt");
      if (File.Exists(deleteFileName)) {
        File.Delete(deleteFileName);
      }
      var newFile = File.CreateText(deleteFileName);

      await newFile.WriteLineAsync(string.Join("\r\n", docNamesToDelete));
      await newFile.FlushAsync();
      newFile.Close();
      return deleteFileName;
    }

    public bool Rewind() {
      if (bDatabaseFailed || (dbInfos?.Count ?? 0) == 0) { return false; }

      //if (dataSet is null) {
      //  if (dbInfos is { }) { RetrieveDataFromDbSets(); }
      //  if (bDatabaseFailed) {
      //    return false;
      //  }

      //  dbInfoIndex = dbInfos.Count + 1;
      //  dbInfoEntityIndex = 1000;
      //  dbInfoRowOffset = RECORDS_PER_RETRIEVAL + 1;
      //  curTableIndex = curRowIndex = 0;
      //  return true;
      //}

      Reset();
      try {

        var deleteFileName = Path.Combine(BaseImageDir, "DeleteList.txt");
        if (File.Exists(deleteFileName)) {
          File.Delete(deleteFileName);
        }
      } catch (Exception) {
        // TODO: Handle the Exception

      }
      return GetMoreRecords() && !bDatabaseFailed;
    }

    public void Reset() {
      dbInfoIndex = dbInfoEntityIndex = dbInfoRowOffset = curTableIndex = curRowIndex = 0;
    }
    #endregion

    #region DB Helpers
    private bool ValidQuery(DbConnection dbConn, string query) {
      // TODO: Perform sql validation
      return true;
    }

    private void RetrieveDataFromDbSets() {
      dataSet = new DataSet();

      foreach (var curDbInfo in dbInfos) {
        var context = curDbInfo.DbContext;
        using (var dbConn = context.Database.GetDbConnection()) {
          dbConn.Open();
          using (var command = dbConn.CreateCommand()) {
            foreach (var curTable in curDbInfo.Tables) {
              try {
                var query = $"SELECT {string.Join(',', curTable.FieldsToIndex.Select(f => GetEscapedValue(f)).ToList())} FROM {GetEscapedValue(curTable.TableName)}";
                if (!ValidQuery(dbConn, query)) {
                  ErrorMessage += $"{(!string.IsNullOrWhiteSpace(ErrorMessage) ? "\r\n" : "")}Error while attempting to generate the query to perform for the index: Potentially unsafe SQL was generated.\r\n'{query}'";
                  continue;
                }
#pragma warning disable CA2100 // Reviewed SQL queries for security vulnerabilities
                command.CommandText = query;
#pragma warning restore CA2100 // Reviewed SQL queries for security vulnerabilities
                command.CommandType = CommandType.Text;

                using var reader = command.ExecuteReader();
                var table = new DataTable() { TableName = $"[{curDbInfo.DbName}.{curTable.TableName}]" };
                table.Load(reader);
                dataSet.Tables.Add(table);
              } catch (Exception ex) {
                ErrorMessage += $"{(!string.IsNullOrWhiteSpace(ErrorMessage) ? "\r\n" : "")}Error while attempting to get data from {curTable}: {ex.Message}";
              }
            }
          }
          if (dbConn is { } && dbConn.State == ConnectionState.Open) { dbConn.Close(); }
        }
      }
    }

    public bool GetMoreRecords() {
      if (dataSet is null) {
        dataSet = new DataSet();
      }
      dataSet.Tables.Clear();
      GC.Collect();
      var myRecCount = 0;
      while (myRecCount < recordsPerRetrieval && dbInfoIndex < dbInfos.Count) {
        var curDbInfo = dbInfos[dbInfoIndex];
        if (dbInfoEntityIndex >= (curDbInfo.Tables?.Count ?? 0)) {
          dbInfoIndex++;
          dbInfoEntityIndex = 0;
          dbInfoRowOffset = 0;
          continue;
        }
        var context = curDbInfo.DbContext;
        var dbConn = context.Database.GetDbConnection();
        if (dbConn.State != ConnectionState.Open) { dbConn.Open(); }
        using (var command = dbConn.CreateCommand()) {
          while (myRecCount < recordsPerRetrieval && dbInfoEntityIndex < curDbInfo.Tables.Count) {
            var curLimit = recordsPerRetrieval - myRecCount;
            var curTable = curDbInfo.Tables[dbInfoEntityIndex];
            var orderBy = string.Join(",", curTable.KeyColumns.Select(k => GetEscapedValue(k)));
            try {
              // We will want to Order By when using LIMIT and OFFSET
              var q = new StringBuilder();
              q.Append($"SELECT {string.Join(',', curTable.FieldsToIndex.Select(f => GetEscapedValue(f)).ToList())} FROM {GetEscapedValue(curTable.TableName)} ");
              if (curDbInfo.UpdateMarkedAsNeedsIndexing) {
                q.Append("WHERE [NeedsIndexing] = 1 ");
              } else if (curDbInfo.RemoveListOfDocuments) {
                q.Append("WHERE 0 = 1 ");
              }
              q.Append($"ORDER BY {orderBy} OFFSET {dbInfoRowOffset} ROWS FETCH NEXT {curLimit} ROWS ONLY");
              if (!ValidQuery(dbConn, q.ToString())) {
                ErrorMessage += $"{(!string.IsNullOrWhiteSpace(ErrorMessage) ? "\r\n" : "")}Error while attempting to generate the query to perform for the index: Potentially unsafe SQL was generated.\r\n'{q}'";
                continue;
              }
#pragma warning disable CA2100 // Reviewed SQL queries for security vulnerabilities
              command.CommandText = q.ToString();
#pragma warning restore CA2100 // Reviewed SQL queries for security vulnerabilities
              command.CommandType = CommandType.Text;
              command.CommandTimeout = 60 * 10;
              using var reader = command.ExecuteReader();
              var table = new DataTable() { TableName = $"[{curDbInfo.DbName}.{curTable.TableName}]" };
              table.Load(reader);
              dataSet.Tables.Add(table);
              myRecCount += table.Rows.Count;
              dbInfoRowOffset += table.Rows.Count;
              if (myRecCount < recordsPerRetrieval) {
                dbInfoEntityIndex++;
                dbInfoRowOffset = 0;
              }
            } catch (Exception ex) {
              ErrorMessage += $"{(!string.IsNullOrWhiteSpace(ErrorMessage) ? "\r\n" : "")}Error while attempting to get data from {curTable}: {ex.Message}";
              bDatabaseFailed = true;
              return false;
            }
          }
          if (myRecCount < recordsPerRetrieval && dbInfoEntityIndex < curDbInfo.Tables.Count) {
            dbInfoIndex++;
            dbInfoEntityIndex = 0;
            dbInfoRowOffset = 0;
          }
        }
        if (dbConn is { } && dbConn.State == ConnectionState.Open) { dbConn.Close(); }
      }
      return dataSet.Tables.Count > 0;
    }

    public static string GetEscapedValue(string forText) {
      using (var builder = new SqlCommandBuilder()) {
        return builder.QuoteIdentifier(forText);
      }
    }

    private string DateToString(DateTime dt) {
      if (dt.Hour == 0 && dt.Minute == 0 && dt.Second == 0) {
        return dt.ToShortDateString();
      }

      return dt.ToString();
    }
    #endregion

    #region Additional Methods
    public int GetDocId(string name) {
      var sj = new SearchJob() {
        IndexesToSearch = new List<string>() { IndexPath }
      };

      sj.Request = string.Join(" AND ", name.Replace("=", "::").Split('#').Last().Split('|'));

      var sr = new SearchResults();
      try {
        sj.Execute(sr);
        if (sr.Count > 0) {
          sr.GetNthDoc(0);
          return sr.CurrentItem.DocId;
        }
      } catch (Exception ex) {
        Console.WriteLine(ex);
      }

      return 0;
    }

    public async Task<bool> GetDocByName(string name) {
      if (!name.StartsWith("db://") || !name.Contains("#")) {
        return false;
      }

      var dbName = GetEscapedValue(name.Split('/').Last().Split('#').First());
      var table = GetEscapedValue(dbName.Split('.').Last().Replace("]", ""));
      dbName = GetEscapedValue(dbName.Split('.').First().Replace("[", ""));

      var fieldVals = name.Split('#').Last().Split('|');
      var vals = new List<string>();
      var parmWhere = new StringBuilder();
      var i = 0;
      foreach (var fieldVal in fieldVals) {
        parmWhere.Append($"{(parmWhere.Length > 0 ? " AND " : "")}{GetEscapedValue(fieldVal.Split('=').First())} = @p{i++}");
      }

      try {
        // TODO: Rework to parse the name
        var dbInfo = dbInfos.SingleOrDefault(dbi => dbi.DbName == dbName);
        var context = dbInfo.DbContext;
        using (var command = context.Database.GetDbConnection().CreateCommand()) {
#pragma warning disable CA2100 // Reviewed SQL queries for security vulnerabilities
          command.CommandText = $"SELECT * FROM {table} WHERE {parmWhere}";
#pragma warning restore CA2100 // Reviewed SQL queries for security vulnerabilities
          command.CommandType = CommandType.Text;
          i = 0;
          foreach (var fieldVal in fieldVals) {
            var parm = command.CreateParameter();
            parm.ParameterName = $"@p{i++}";
            parm.Value = fieldVal.Split('=').Last();
          }

          using (var reader = await command.ExecuteReaderAsync()) {
            var dTable = new DataTable();
            dTable.Load(reader);
            dataSet = new DataSet();
            dataSet.Tables.Add(dTable);
          }
        }
      } catch (Exception ex) {
        ErrorMessage = ex.Message;
        bDatabaseFailed = false;
        return false;
      }

      if (dataSet.Tables.Count == 0 || dataSet.Tables[0].Rows.Count == 0 || dataSet.Tables[0].Columns.Count == 0) { return false; }

      ErrorMessage = "";
      bDatabaseFailed = false;
      curTableIndex = 0;
      curRowIndex = 0;

      return GetNextDoc();
    }
    #endregion
  }
}
