namespace Swick.Obsidian.SyncManager.Web;

public interface IUserSecretProvider
{
    string DeriveUserPassword(string sub);
}
