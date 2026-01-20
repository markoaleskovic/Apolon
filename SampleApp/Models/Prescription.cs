using ORM.Core.LazyLoading;
using ORM.Core.Mapping.Attributes;

namespace SampleApp.Models;

[Table("prescriptions")]
public class Prescription : IHasLazyLoader
{
    private ILazyLoader? _lazy;
    public void SetLazyLoader(ILazyLoader loader) => _lazy = loader;

    [Key]
    [DatabaseGeneratedIdentity]
    [Column("id")]
    public long Id { get; set; }

    [Required]
    [Column("checkup_id")]
    public long CheckupId { get; set; }

    [Required]
    [Column("medication_id")]
    public long MedicationId { get; set; }

    [Required]
    [Column("dosage")]
    public string Dosage { get; set; } = "";

    [Required]
    [Column("start_date")]
    public DateTime StartDate { get; set; }

    [Column("end_date")]
    public DateTime? EndDate { get; set; }

    [ForeignKey(nameof(CheckupId))]
    [InverseProperty(nameof(Checkup.Prescriptions))]
    public virtual Checkup? Checkup
        => _lazy?.LoadReference<Checkup>(this, nameof(Checkup));

    // no inverse collection needed for demo, but reference needed
    [ForeignKey(nameof(MedicationId))]
    public virtual Medication? Medication
        => _lazy?.LoadReference<Medication>(this, nameof(Medication));
}