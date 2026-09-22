using Joblink.Services.JobSearch;
using Joblink.Services.Resume;
using JobLinkv2.Services.Resumes.Export;

namespace Joblink.Tests.Support
{
    // Stands in for JobLink-AI (Python) so an endpoint test never needs it running. Records every
    // resume it was asked to render, in order, whichever format.
    public sealed class FakeResumeDocumentService : IResumeDocumentService
    {
        public List<ResumeDataDto> PdfRequests { get; } = new();

        public List<ResumeDataDto> DocxRequests { get; } = new();

        // Set to make the next call fail the way the real service can (a bad status + message).
        public UpstreamResult<GeneratedDocument>? Fail { get; set; }

        public Task<UpstreamResult<GeneratedDocument>> GeneratePdfAsync(ResumeDataDto resume, CancellationToken cancellationToken)
        {
            PdfRequests.Add(resume);

            return Task.FromResult(Fail ?? UpstreamResult<GeneratedDocument>.Success(
                new GeneratedDocument(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf", "resume.pdf")));  // "%PDF"
        }

        public Task<UpstreamResult<GeneratedDocument>> GenerateDocxAsync(ResumeDataDto resume, CancellationToken cancellationToken)
        {
            DocxRequests.Add(resume);

            return Task.FromResult(Fail ?? UpstreamResult<GeneratedDocument>.Success(
                new GeneratedDocument(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "resume.docx")));  // "PK\x03\x04"
        }
    }
}
