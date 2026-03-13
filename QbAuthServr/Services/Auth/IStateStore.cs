namespace QbAuthServr.Services.Auth;

public interface IStateStore
{
    string Create();
    bool ValidateAndConsume(string? state);
}