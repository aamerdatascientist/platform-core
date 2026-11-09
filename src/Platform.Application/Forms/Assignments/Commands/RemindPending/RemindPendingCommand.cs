using MediatR;
using Platform.Application.Forms.Assignments;
using Platform.Application.Notifications;

namespace Platform.Application.Forms.Assignments.Commands.RemindPending;

/// <summary>
/// "Remind those who haven't filled (n)" on the tracking page (redesign 23, HANDOFF §9.5):
/// reminds everyone still pending today through the existing sender. Manual reminders go at most
/// once per person, form and day per channel, apart from the automatic ones (owner decision 7).
/// </summary>
public record RemindPendingCommand : IRequest<AssignmentNotifier.ManualReminderResult>;

public class RemindPendingCommandHandler : IRequestHandler<RemindPendingCommand, AssignmentNotifier.ManualReminderResult>
{
    private readonly AssignmentNotifier _notifier;
    public RemindPendingCommandHandler(AssignmentNotifier notifier) => _notifier = notifier;

    public Task<AssignmentNotifier.ManualReminderResult> Handle(RemindPendingCommand request, CancellationToken cancellationToken) =>
        _notifier.SendManualRemindersAsync(FormAssignmentTracker.SaudiToday(DateTime.UtcNow), cancellationToken);
}
