using ORM.Core.Mapping.Attributes;

namespace SampleApp.Models;

[Table("medications")]
public class Medication
{
    [Key]
    [DatabaseGeneratedIdentity]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Unique]
    [Column("name")]
    public string Name { get; set; } = "";

    [Column("atc_code")]
    public string? AtcCode { get; set; }

    [Required]
    [DefaultValue("")]
    [Column("default_dosage")]
    public string DefaultDosage { get; set; } = "";
}