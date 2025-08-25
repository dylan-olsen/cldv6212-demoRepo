namespace retailMvcDemo.Models
{
    public class ContractFileViewModel
    {
        public string Name { get; set; } = "";
        public long Bytes { get; set; }
        public System.DateTimeOffset? LastModified { get; set; }
    }
}
