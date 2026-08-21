using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Wasta.Application.Features.Applications;
using Wasta.Application.Features.Assessments;
using Wasta.Application.Features.Auth;
using Wasta.Application.Features.Credits;
using Wasta.Application.Features.Files;
using Wasta.Application.Features.Notifications;
using Wasta.Application.Features.Jobs;
using Wasta.Application.Features.Localization;
using Wasta.Application.Features.TalentPool;

namespace Wasta.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers use-case handlers and their validators. Handlers are plain
    /// classes resolved directly - no mediator, because a dispatcher would add
    /// indirection without removing any work here.
    /// </summary>
    public static IServiceCollection AddWastaApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<LoginValidator>();

        services.AddScoped<RegisterSeekerHandler>();
        services.AddScoped<RegisterCompanyHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshHandler>();
        services.AddScoped<RequestEmailVerificationHandler>();
        services.AddScoped<ConfirmEmailHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<ExportPersonalDataHandler>();
        services.AddScoped<DeleteAccountHandler>();

        services.AddScoped<StartAttemptHandler>();
        services.AddScoped<GetAttemptHandler>();
        services.AddScoped<SaveAnswerHandler>();
        services.AddScoped<SubmitAttemptHandler>();
        services.AddScoped<GetResultsHandler>();

        services.AddScoped<PostJobHandler>();
        services.AddScoped<UpdateJobHandler>();
        services.AddScoped<CloseJobHandler>();
        services.AddScoped<BrowseJobsHandler>();
        services.AddScoped<GetJobDetailHandler>();
        services.AddScoped<ListCompanyJobsHandler>();
        services.AddScoped<GetJobApplicantsHandler>();

        services.AddScoped<ApplyToJobHandler>();
        services.AddScoped<UpdateProjectHandler>();
        services.AddScoped<SubmitProjectHandler>();
        services.AddScoped<WithdrawApplicationHandler>();
        services.AddScoped<ListMyApplicationsHandler>();
        services.AddScoped<GetMyApplicationHandler>();
        services.AddScoped<SetApplicationStatusHandler>();

        services.AddScoped<BrowseTalentPoolHandler>();
        services.AddScoped<GetCandidateHandler>();
        services.AddScoped<UnlockCandidateHandler>();

        services.AddScoped<GetLedgerHandler>();
        services.AddScoped<ListMyTopUpRequestsHandler>();
        services.AddScoped<RequestTopUpHandler>();
        services.AddScoped<ListPendingCompaniesHandler>();
        services.AddScoped<ListPendingTopUpsHandler>();
        services.AddScoped<ApproveCompanyHandler>();
        services.AddScoped<RejectCompanyHandler>();
        services.AddScoped<ReviewTopUpHandler>();

        services.AddScoped<UploadCvHandler>();
        services.AddScoped<UploadApplicationFileHandler>();
        services.AddScoped<UploadCompanyDocumentHandler>();

        services.AddScoped<ListNotificationsHandler>();
        services.AddScoped<UnreadCountHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<MarkAllNotificationsReadHandler>();

        services.AddScoped<GetReferenceDataHandler>();
        services.AddScoped<SetLanguageHandler>();

        return services;
    }
}
