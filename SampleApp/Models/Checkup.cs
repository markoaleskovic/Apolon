using ORM.Core.LazyLoading;
using ORM.Core.Mapping.Attributes;

namespace SampleApp.Models;

[Table("checkups")]
public class Checkup : IHasLazyLoader
{
    private ILazyLoader? _lazy;
    public void SetLazyLoader(ILazyLoader loader) => _lazy = loader;

    [Key]
    [DatabaseGeneratedIdentity]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Column("patient_id")]
    public long PatientId { get; set; }

    [Required]
    [Column("checkup_type")]
    public string CheckupType { get; set; }

    [Column("performed_at")]
    public DateTime PerformedAt { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("body_temp_c")]
    public float? BodyTempC { get; set; }

    [ForeignKey(nameof(PatientId))]
    [InverseProperty(nameof(Patient.Checkups))]
    public virtual Patient? Patient
        => _lazy?.LoadReference<Patient>(this, nameof(Patient));

    [InverseProperty(nameof(Prescription.Checkup))]
    public virtual IReadOnlyList<Prescription> Prescriptions
        => _lazy?.LoadCollection<Prescription>(this, nameof(Prescriptions)) ?? Array.Empty<Prescription>();
}