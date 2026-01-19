using ORM.Core.Mapping.Attributes;

namespace ORM.Core;

[Table("patients")]
public class Patient
{
    [Key, DatabaseGeneratedIdentity]
    [Column("id")]
    public int Id { get; set; }

    [Column("first_name")] 
    public string FirstName { get; set; } = "";
}