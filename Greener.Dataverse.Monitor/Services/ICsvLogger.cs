using Greener.Dataverse.Monitor.Models;

namespace Greener.Dataverse.Monitor.Services;

public interface ICsvLogger
{
    void EnsureHeader();
    void Append(CsvRow row);
}
