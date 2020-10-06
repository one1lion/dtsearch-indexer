using System;
using System.Collections.Generic;
using System.Text;

namespace DtSearchIndexer.Database.Models {
  public class RandomStuffToIndex {
    public int Id { get; set; }
    public string LongTextOfStuff { get; set; }
    public string AssociatedAnimal { get; set; }
    public string Title { get; set; }
    public string GivenName { get; set; }
    public string MiddleInitial { get; set; }
    public string Surname{ get; set; }
  }
}
