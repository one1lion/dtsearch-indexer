namespace DtSearchIndexer.IndexerUtility.Models {
  public class ProgressInfo {
    public string FileName { get; set; }
    public uint CurrMergePercent { get; set; }
    public uint EstRemainingSeconds { get; set; }
    public uint ElapsedSeconds { get; set; }
    public ulong FilesChecked { get; set; }
    public ulong FilesToCheck { get; set; }
    public ulong DocsRead { get; set; }
    public ulong FilesRead { get; set; }
    public ulong FilesToIndex { get; set; }
    public ulong DocsInIndex { get; set; }
    public ulong WordsInIndex { get; set; }
    public ulong EncryptedCount { get; set; }
    public ulong OpenFailures { get; set; }
    public ulong BinaryCount { get; set; }
    public int IndexPercentFull { get; set; }
    public ulong BytesRead64 { get; set; }
    public ulong BytesToIndex64 { get; set; }
    public ulong DocBytesRead64 { get; set; }
    public ulong BytesReadKB { get; set; }
    public ulong BytesToIndexKB { get; set; }
    public ulong DocBytesReadKB { get; set; }
    public int PercentDone { get; set; }
    public string IndexPath { get; set; }
    public ProgressStep Step { get; set; }
    public ProgressMessageCode UpdateType { get; set; }
    public ulong PartiallyEncryptedCount { get; set; }
    public ulong PartiallyCorruptCount { get; set; }
  }

  public enum ProgressStep {
    ixStepNone = 0,
    ixStepBegin = 1,
    ixStepCreatingIndex = 2,
    ixStepCheckingFiles = 3,
    ixStepReadingFiles = 4,
    ixStepStoringWords = 5,
    ixStepMerging = 6,
    ixStepCompressing = 7,
    ixStepDone = 8,
    ixStepVerifyingIndex = 9,
    ixStepMergingIndexes = 10,
    ixStepRemovingDeletedFiles = 11,
    ixStepCommittingChanges = 12
  }

  public enum ProgressMessageCode {
    dtsnFirstStatusMessage = 1000,
    dtsnCheckForAbort = 1001,
    dtsnJobClose = 1003,
    dtsnConvertPercentDone = 1004,
    dtsnFirstSearchStatusMessage = 2000,
    dtsnSearchBegin = 2001,
    dtsnSearchDone = 2002,
    dtsnSearchWhere = 2003,
    dtsnSearchFound = 2004,
    dtsnSearchUpdateTime = 2005,
    dtsnSearchFileEncrypted = 2006,
    dtsnSearchFileCorrupt = 2007,
    dtsnSearchFileDone = 2008,
    dtsnLastSearchStatusMessage = 2999,
    dtsnFirstIndexStatusMessage = 3000,
    dtsnIndexBegin = 3001,
    dtsnIndexDone = 3002,
    dtsnIndexCreate = 3003,
    dtsnIndexCheckingFiles = 3004,
    dtsnIndexToAddUpdate = 3005,
    dtsnIndexAdded = 3006,
    dtsnIndexStartingFile = 3007,
    dtsnIndexFileProgress = 3008,
    dtsnIndexFileDone = 3009,
    dtsnIndexFileOpenFail = 3010,
    dtsnIndexFileBinary = 3011,
    dtsnIndexMergeProgress = 3012,
    dtsnIndexCompressProgress = 3013,
    dtsnIndexFileEncrypted = 3014,
    dtsnIndexStoringWords = 3015,
    dtsnIndexStartingUpdate = 3016,
    dtsnIndexFilePartiallyEncrypted = 3017,
    dtsnIndexFilePartiallyCorrupt = 3018,
    dtsnAutoCommitBegin = 3019,
    dtsnAutoCommitDone = 3020,
    dtsnIndexDeletedFileRemoved = 3021,
    dtsnIndexListedFileRemoved = 3022,
    dtsnIndexListedFileNotRemoved = 3023,
    dtsnIndexFolderInaccessible = 3024,
    dtsnLastIndexStatusMessage = 3999,
    dtsnIndexMergeJobProgress = 4000,
    dtsnIndexVerifyProgress = 4001
  }
}
