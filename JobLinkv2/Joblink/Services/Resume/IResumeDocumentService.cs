using Joblink.Services.JobSearch;
using JobLinkv2.Services.Resumes.Export;

namespace Joblink.Services.Resume
{
    // A generated file, ready to hand straight to the browser.
    public sealed record GeneratedDocument(byte[] Bytes, string ContentType, string FileName);

    // Turns a resume into a downloadable document. JobLink-AI (Python) is the only implementation
    // today; UpstreamResult is the same result type IJobSearchService uses, so a controller handles
    // a failure from either service the same way.
    public interface IResumeDocumentService
    {
        Task<UpstreamResult<GeneratedDocument>> GeneratePdfAsync(ResumeDataDto resume, CancellationToken cancellationToken);

        Task<UpstreamResult<GeneratedDocument>> GenerateDocxAsync(ResumeDataDto resume, CancellationToken cancellationToken);
    }
}
