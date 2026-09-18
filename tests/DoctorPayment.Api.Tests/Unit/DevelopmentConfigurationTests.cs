using Xunit;

namespace DoctorPayment.Api.Tests.Unit;

public sealed class DevelopmentConfigurationTests
{
    [Fact]
    public void DevelopmentConfiguration_DoesNotContainSecrets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configurationPath = Path.Combine(
            repositoryRoot,
            "src",
            "DoctorPayment.Api",
            "appsettings.Development.json");

        var content = File.ReadAllText(configurationPath);

        Assert.DoesNotContain("Password=", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret\": \"", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DoctorPayment.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("DoctorPayment.sln was not found.");
    }
}
