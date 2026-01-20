using ORM.Core.LazyLoading;
using ORM.Core.Mapping.Attributes;

namespace SampleApp.Models;

[Table("patients")]
public class Patient : IHasLazyLoader
{
    private ILazyLoader? _lazy;
    public void SetLazyLoader(ILazyLoader loader) => _lazy = loader;

    [Key]
    [DatabaseGeneratedIdentity]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Column("first_name")]
    public string FirstName { get; set; } = "";

    [Required]
    [Column("last_name")]
    public string LastName { get; set; } = "";

    [Required]
    [Unique]
    [Column("oib")]
    public string Oib { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    // 1-1
    [InverseProperty("Patient")]
    public virtual MedicalRecord? MedicalRecord
        => _lazy?.LoadReference<MedicalRecord>(this, nameof(MedicalRecord));

    // 1-many
    [InverseProperty("Patient")]
    public virtual IReadOnlyList<Checkup> Checkups
        => _lazy?.LoadCollection<Checkup>(this, nameof(Checkups)) ?? Array.Empty<Checkup>();
}