using PixousDocIntelligence.Domain.Contracts;

namespace PixousDocIntelligence.Pipeline.Verification;

/// <summary>
/// Service responsible for Phase 6: Deterministic evidence verification, confidence calibration,
/// and review queue generation.
/// </summary>
public interface IEvidenceVerifier
{
    /// <summary>
    /// Verifies source evidence quotations against physical PDF page text,
    /// applies confidence calibration rules, and constructs the human review queue.
    /// </summary>
    Task<VerificationReport> VerifyEvidenceAndCalibrateAsync(
        string pdfPath, 
        DocumentEnvelope envelope, 
        QualityPolicy? policy = null, 
        CancellationToken cancellationToken = default);
}
