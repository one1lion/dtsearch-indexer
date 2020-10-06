using dtSearch.Engine;

namespace DtSearchIndexer.IndexerUtility.Handlers {
  public class IndexStatusHandler : IIndexStatusHandler {
    private readonly DtSearchIndexer dtSearchIndexer;

    public IndexStatusHandler(DtSearchIndexer dtSearchIndexer) {
      this.dtSearchIndexer = dtSearchIndexer;
    }

    public AbortValue CheckForAbort() {
      if (dtSearchIndexer.StopPressed) { return dtSearchIndexer.AbortImmediately ? AbortValue.CancelImmediately : AbortValue.Cancel; }
      return AbortValue.Continue;
    }

    public void OnProgressUpdate(IndexProgressInfo info) {
      dtSearchIndexer.HandleProgressChange(info);
    }
  }
}
