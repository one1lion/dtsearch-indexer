using dtSearch.Engine;
using DtSearchIndexer.IndexerUtility.DataSources;
using DtSearchIndexer.IndexerUtility.Handlers;
using DtSearchIndexer.IndexerUtility.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DtSearchIndexer.IndexerUtility {
  // More information on incremental updating: https://support.dtsearch.com/faq/dts0111.htm
  public class DtSearchIndexer {
    private readonly ILogger logger;
    private readonly int recsPerRetrieval;

    public event EventHandler<ProgressInfo> OnProgressChanged;
    public DtSearchIndexer() { }

    public DtSearchIndexer(ILogger passedLoogger, int recsPerRetrieval = 150000) {
      this.logger = passedLoogger;
      this.recsPerRetrieval = recsPerRetrieval;
    }

    public DtSearchIndexer(ILogger<DtSearchIndexer> logger, int recsPerRetrieval = 150000) {
      this.logger = logger;
      this.recsPerRetrieval = recsPerRetrieval;
    }

    public string BaseImageDir { get; set; }
    public bool CreateOrRecreateFullIndex { get; private set; }
    public bool UpdateExistingSavedIndexes { get; private set; }
    public bool RemoveMissingDocuments { get; private set; }
    public bool UpdateMarkedAsNeedsIndexing { get; private set; }
    public bool RemoveOnly { get; private set; }
    public List<string> ManualRemoveDocList { get; private set; }
    public bool StopPressed { get; private set; }
    public bool AbortImmediately { get; private set; }
    public bool Indexing { get; private set; }
    public bool CloseRequested { get; private set; }
    public string CurrentIndexName { get; set; }
    public int IndexRequestId { get; set; }

    public List<IndexedItem> IndexedItems { get; set; } = new List<IndexedItem>();
    public string BaseImageDir_Test { get; set; }
    public bool IsTest { get; set; }
    private long recCount;

    public async Task IndexDatabase(DbInfo dbInfo, List<string> manualRemoveDocList = null) {
      await IndexDatabase(dbInfo.IndexPath, dbInfo.StoredFields, new List<DbInfo>() { dbInfo }, manualRemoveDocList);
    }

    public async Task IndexDatabase(string indexPath, List<string> storedFields, List<DbInfo> dbInfos, List<string> manualRemoveDocList = null) {
      using var ij = new IndexJob();
      CreateOrRecreateFullIndex = dbInfos.Any(dbi => dbi.CreateOrRecreateFullIndex);
      UpdateExistingSavedIndexes = dbInfos.Any(dbi => dbi.UpdateExistingSavedIndexes);
      RemoveMissingDocuments = dbInfos.Any(dbi => dbi.RemoveMissingDocuments);
      UpdateMarkedAsNeedsIndexing = dbInfos.Any(dbi => dbi.UpdateMarkedAsNeedsIndexing);
      RemoveOnly = dbInfos.Any(dbi => dbi.RemoveListOfDocuments && !(UpdateExistingSavedIndexes || UpdateMarkedAsNeedsIndexing));
      ManualRemoveDocList = manualRemoveDocList;
      StopPressed = false;
      AbortImmediately = false;
      Indexing = true;

      var dataSource = new DbDataSource(dbInfos, recsPerRetrieval) {
        BaseImageDir = BaseImageDir,
        IsTest = IsTest,
        BaseImageDir_Test = BaseImageDir_Test,
        IndexPath = indexPath
      };
      ij.DataSourceToIndex = dataSource;
      ij.IndexPath = indexPath;
      //ij.CreateRelativePaths = false;
      ij.StoredFields = storedFields;
      ij.IndexingFlags =
        // Compress and store the documents in the index (for highlighting hits)
        IndexingFlags.dtsIndexCacheOriginalFile |
        // Compress and store document text in the index (for generating hits-in-context 
        // snippets to include in search results)
        IndexingFlags.dtsIndexCacheText |
        // Prevents fields added with DataSource.DocFields from being included in cached text
        IndexingFlags.dtsIndexCacheTextWithoutFields;

      dataSource.Reset();

      //if (UpdateMarkedAsNeedsIndexing || RemoveOnly) {
      //  ij.ActionAdd = false;
      //  ij.ActionRemoveListed = true;
      //  ij.ToRemoveListName = await dataSource.CreateRemoveListForUpdateRequest(ManualRemoveDocList);
      //  ExecuteIndexJob(ij);
      //  dataSource.IndexedItems.Clear();
      //  ij.ActionRemoveListed = false;
      //  dataSource.Reset();
      //}
      //
      //if (!RemoveOnly) { 
      //  ij.ActionCreate = CreateOrRecreateFullIndex;
      //  ij.ActionRemoveDeleted = RemoveMissingDocuments;
      //  //ij.ActionMerge = UpdateExistingSavedIndexes || UpdateMarkedAsNeedsIndexing;
      //  ij.ActionAdd = true;
      //  // Execute the index job
      //  ExecuteIndexJob(ij);
      //}

      if (RemoveOnly) {
        ij.ActionAdd = false;
        ij.ActionRemoveListed = true;
        ij.ToRemoveListName = await dataSource.CreateRemoveListForUpdateRequest(ManualRemoveDocList);
        ExecuteIndexJob(ij);
        dataSource.IndexedItems.Clear();
        ij.ActionRemoveListed = false;
        dataSource.Reset();
      } else {
        ij.ActionCreate = CreateOrRecreateFullIndex;
        ij.ActionRemoveDeleted = RemoveMissingDocuments;
        //ij.ActionMerge = UpdateExistingSavedIndexes || UpdateMarkedAsNeedsIndexing;
        ij.ActionAdd = true;
        // Execute the index job
        ExecuteIndexJob(ij);
      }
      if ((dataSource.IndexedItems?.Count ?? 0) > 0) { IndexedItems.AddRange(dataSource.IndexedItems); }
    }

    // Executes the index job ij in new thread and keeps track of its progress 
    private void ExecuteIndexJob(IndexJob ij) {
      // Set the status variables
      Indexing = true;

      // Start index job execution in a separate thread
      // In ASP.NET applications, use Execute, not ExecuteInThread
      var statusHandler = new IndexStatusHandler(this);
      ij.StatusHandler = statusHandler;
      try {
        ij.Execute();
      } catch (Exception ex) {
        if (logger is { }) { logger.LogError(ex, $"An error occurred while trying to execute the index job: {ex.Message}"); }
      }
      var dataSource = (DbDataSource)(ij.DataSourceToIndex);
      if (dataSource.WasError) {
        // TODO: Do special stuff with error message
      }

      // Update the status text based on the reason the while loop ended
      if (AbortImmediately) {
        //Status.Text = "Indexing halted, index not updated";
        if (logger is { }) { logger.LogInformation("Indexing halted, index not updated"); }
      } else if (StopPressed) {
        //Status.Text = "Indexing halted, index partially updated";
        if (logger is { }) { logger.LogInformation("Indexing halted, index partially updated"); }
      } else {
        //Status.Text = "Indexing Complete";
        if (logger is { }) { logger.LogInformation("Indexing Complete"); }
      }

      // Reset flags and controls
      Indexing = false;

      // If there were errors, display the errors as additions to the
      // status text
      var err = ij.Errors;
      for (int i = 0; i < err.Count; i++) {
        //Status.Text = Status.Text + " " + err.Message(i);
        if (logger is { }) { logger.LogError($"Errors occurred while processing the index job: {err.Message(i)}"); }
      }
    }

    public void HandleProgressChange(IndexProgressInfo status) {
      // Set the status text based on the current indexing step
      switch (status.Step) {
        case IndexingStep.ixStepBegin:
          //Status.Text = "Opening index";
          if (logger is { }) { logger.LogInformation("Opening index"); }
          break;

        case IndexingStep.ixStepCheckingFiles:
          //Status.Text = "Checking files";
          if (logger is { }) { logger.LogInformation("Checking files"); }
          break;

        case IndexingStep.ixStepCompressing:
          //Status.Text = "Compressing index";
          if (logger is { }) { logger.LogInformation("Compressing index"); }
          break;

        case IndexingStep.ixStepCreatingIndex:
          //Status.Text = "Creating index";
          if (logger is { }) { logger.LogInformation("Creating index"); }
          break;

        case IndexingStep.ixStepDone:
          //Status.Text = "Indexing Complete";
          if (logger is { }) { logger.LogInformation("Current step is complete"); }
          recCount = 0;
          break;
        case IndexingStep.ixStepMerging:
          //Status.Text = "Merging words into index";
          if (logger is { }) { logger.LogInformation("Merging words into index"); }
          break;

        case IndexingStep.ixStepNone:
          if (logger is { }) { logger.LogInformation("No Step"); }
          break;

        case IndexingStep.ixStepReadingFiles:
          //Status.Text = status.File.Name;
          if ((++recCount - 1) % 100 == 0 && logger is { }) {
            logger.LogInformation($"Reading Files: {recCount} file{(recCount - 1 != 1 ? "s" : "")} read");
          }
          break;

        case IndexingStep.ixStepStoringWords:
          //Status.Text = status.File.Name + " (storing words)";
          if ((++recCount - 1) % 100 == 0 && logger is { }) {
            logger.LogInformation($"Storing Words: {recCount} word{(recCount - 1 != 1 ? "s" : "")} stored");
          }
          break;

        default:
          logger.LogInformation($"Performing step: {status.Step}");
          break;
      }
      OnProgressChanged?.Invoke(this, new ProgressInfo() {
        FileName = status.File?.Name,
        CurrMergePercent = status.CurrMergePercent,
        EstRemainingSeconds = status.EstRemainingSeconds,
        ElapsedSeconds = status.ElapsedSeconds,
        FilesChecked = status.FilesChecked,
        FilesToCheck = status.FilesToCheck,
        DocsRead = status.DocsRead,
        FilesRead = status.FilesRead,
        FilesToIndex = status.FilesToIndex,
        DocsInIndex = status.DocsInIndex,
        WordsInIndex = status.WordsInIndex,
        EncryptedCount = status.EncryptedCount,
        OpenFailures = status.OpenFailures,
        BinaryCount = status.BinaryCount,
        IndexPercentFull = status.IndexPercentFull,
        BytesRead64 = status.BytesRead64,
        BytesToIndex64 = status.BytesToIndex64,
        DocBytesRead64 = status.DocBytesRead64,
        BytesReadKB = status.BytesReadKB,
        BytesToIndexKB = status.BytesToIndexKB,
        DocBytesReadKB = status.DocBytesReadKB,
        PercentDone = status.PercentDone,
        IndexPath = status.IndexPath,
        Step = Enum.Parse<ProgressStep>(((int)status.Step).ToString()),
        UpdateType = Enum.Parse<ProgressMessageCode>(((int)status.UpdateType).ToString()),
        PartiallyEncryptedCount = status.PartiallyEncryptedCount,
        PartiallyCorruptCount = status.PartiallyCorruptCount
      });
    }
  }
}
