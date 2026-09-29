namespace AgySwitch.Services;

public static class FilePairTransaction
{
    public static void Apply(string stagingRoot, IReadOnlyList<(string? Source, string Destination)> files)
    {
        Directory.CreateDirectory(stagingRoot);
        var transactionDirectory = Path.Combine(stagingRoot, ".agy-txn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transactionDirectory);
        var stagedSources = new string?[files.Count];
        var oldDestinations = new string?[files.Count];
        var destinationExisted = new bool[files.Count];
        var changed = new List<int>();

        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                var (source, destination) = files[index];
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (source is not null)
                {
                    if (!File.Exists(source))
                    {
                        throw new FileNotFoundException("缺少需要安装的认证文件。", source);
                    }

                    var staged = Path.Combine(transactionDirectory, $"new-{index}");
                    File.Copy(source, staged, true);
                    stagedSources[index] = staged;
                }

                if (File.Exists(destination))
                {
                    var previous = Path.Combine(transactionDirectory, $"old-{index}");
                    File.Copy(destination, previous, true);
                    oldDestinations[index] = previous;
                    destinationExisted[index] = true;
                }
            }

            for (var index = 0; index < files.Count; index++)
            {
                var destination = files[index].Destination;
                var staged = stagedSources[index];
                if (staged is null)
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                        changed.Add(index);
                    }
                }
                else
                {
                    ReplaceOne(staged, destination);
                    changed.Add(index);
                }
            }
        }
        catch (Exception operationError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var index in changed.AsEnumerable().Reverse())
            {
                try
                {
                    var destination = files[index].Destination;
                    if (destinationExisted[index] && oldDestinations[index] is { } previous)
                    {
                        var restoreStaged = Path.Combine(transactionDirectory, $"restore-{index}");
                        File.Copy(previous, restoreStaged, true);
                        ReplaceOne(restoreStaged, destination);
                    }
                    else if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new IOException(
                    $"文件操作失败，且有 {rollbackErrors.Count} 项无法自动回滚。请检查目标账户目录和恢复副本。",
                    new AggregateException(rollbackErrors.Prepend(operationError)));
            }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(transactionDirectory))
                {
                    Directory.Delete(transactionDirectory, true);
                }
            }
            catch (IOException)
            {
                // A leftover staging directory is recoverable and contains only file copies.
            }
            catch (UnauthorizedAccessException)
            {
                // A leftover staging directory is recoverable and contains only file copies.
            }
        }
    }

    private static void ReplaceOne(string staged, string destination)
    {
        if (File.Exists(destination))
        {
            File.Replace(staged, destination, null, true);
        }
        else
        {
            File.Move(staged, destination);
        }
    }
}
