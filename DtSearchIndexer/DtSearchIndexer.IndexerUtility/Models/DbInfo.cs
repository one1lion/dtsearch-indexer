using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace DtSearchIndexer.IndexerUtility.Models {
  public class DbInfo {
    public string DbName { get; set; }
    public DbContext DbContext { get; set; }
    public List<DbTableInfo> Tables { get; set; }
    public List<string> StoredFields { get; set; }
    public string IndexPath { get; set; }
    public bool CreateOrRecreateFullIndex { get; set; }
    public bool UpdateMarkedAsNeedsIndexing { get; set; }
    public bool UpdateExistingSavedIndexes { get; set; }
    public bool RemoveMissingDocuments { get; set; }
    public bool RemoveListOfDocuments { get; set; }
  }
}
