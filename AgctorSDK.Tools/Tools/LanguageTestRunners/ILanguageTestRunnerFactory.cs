namespace AgctorSDK.Core.Tools.LanguageTestRunners
{
    /// <summary>
    /// Access point for retrieving or registering test runners for various languages.
    /// </summary>
    public interface ILanguageTestRunnerFactory
    {
        ILanguageTestRunner? GetRunner(string language);
        void RegisterRunner(ILanguageTestRunner runner);
        void RegisterLanguageAlias(string alias, string language);
    }
} 