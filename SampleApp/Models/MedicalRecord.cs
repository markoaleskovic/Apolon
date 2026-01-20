using ORM.Core.LazyLoading;
using ORM.Core.Mapping.Attributes;

namespace SampleApp.Models;

[Table("medical_records")]
public class MedicalRecord : IHasLazyLoader
{
    private ILazyLoader? _lazy;
    public void SetLazyLoader(ILazyLoader loader) => _lazy = loader;

    [Key]
    [DatabaseGeneratedIdentity]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Unique]
    [Column("patient_id")]
    public long PatientId { get; set; }

    [Required]
    [DefaultValue("")]
    [Column("notes")]
    public string Notes { get; set; } = "";

    [ForeignKey(nameof(PatientId))]
    [InverseProperty(nameof(Patient.MedicalRecord))]
    public virtual Patient? Patient
        => _lazy?.LoadReference<Patient>(this, nameof(Patient));
}
