namespace Retail360.Domain.Identity;

public enum UserRuleFailure
{
    Invalid,
    NotFound,
    Conflict
}

public sealed class UserRuleException : Exception
{
    public UserRuleException(UserRuleFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public UserRuleFailure Failure { get; }
}
