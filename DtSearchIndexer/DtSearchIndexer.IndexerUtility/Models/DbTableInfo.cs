using System.Collections.Generic;

namespace DtSearchIndexer.IndexerUtility.Models {
  public class DbTableInfo {
    public string TableName { get; set; }
    public List<string> FieldsToIndex { get; set; }
    public List<string> KeyColumns { get; set; }
  }
}