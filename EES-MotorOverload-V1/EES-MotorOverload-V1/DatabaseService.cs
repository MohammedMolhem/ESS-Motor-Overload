using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.OleDb;

namespace EES_MotorOverload_V1.Services
{
    public class DatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private bool _disposed = false;
        private const string BEARINGS_TABLE = "[Table 2]";

        public DatabaseService(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            _connectionString = connectionString;
            Logger.Info("DatabaseService initialized");
        }

        public async Task<List<BearingModel>> GetAllBearingsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bearings = new List<BearingModel>();
                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info("Database connection opened");
                        string sql = $"SELECT ID, BearNB, NB, BD, PD, PHI, BPFO, BPFI, FTF, BSF FROM {BEARINGS_TABLE}";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.CommandTimeout = 30;
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                    bearings.Add(ReadBearing(reader));
                            }
                        }
                    }
                    Logger.Info($"Retrieved {bearings.Count} bearings from database");
                    return bearings;
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetAllBearingsAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<BearingModel> GetBearingByIdAsync(int id)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info($"Querying bearing ID: {id}");
                        string sql = $"SELECT ID, BearNB, NB, BD, PD, PHI, BPFO, BPFI, FTF, BSF FROM {BEARINGS_TABLE} WHERE ID = @ID";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ID", id);
                            cmd.CommandTimeout = 30;
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    var bearing = ReadBearing(reader);
                                    Logger.Info($"Retrieved bearing ID: {id} - {bearing.BearNB}");
                                    return bearing;
                                }
                            }
                        }
                    }
                    Logger.Warn($"Bearing with ID {id} not found");
                    return null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetBearingByIdAsync failed for ID {id}: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<BearingModel> GetBearingByNameAsync(string bearingName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(bearingName))
                        throw new ArgumentException("Bearing name cannot be null or empty", nameof(bearingName));

                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info($"Querying bearing: {bearingName}");
                        string sql = $"SELECT ID, BearNB, NB, BD, PD, PHI, BPFO, BPFI, FTF, BSF FROM {BEARINGS_TABLE} WHERE BearNB = @BearNB";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@BearNB", bearingName);
                            cmd.CommandTimeout = 30;
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    var bearing = ReadBearing(reader);
                                    Logger.Info($"Retrieved bearing: {bearingName}");
                                    return bearing;
                                }
                            }
                        }
                    }
                    Logger.Warn($"Bearing '{bearingName}' not found");
                    return null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetBearingByNameAsync failed for '{bearingName}': {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<int> AddBearingAsync(BearingModel bearing)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (bearing == null)
                        throw new ArgumentNullException(nameof(bearing));

                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info($"Adding new bearing: {bearing.BearNB}");
                        string sql = $@"INSERT INTO {BEARINGS_TABLE} 
                            (BearNB, NB, BD, PD, PHI, BPFO, BPFI, FTF, BSF) 
                            VALUES (@BearNB, @NB, @BD, @PD, @PHI, @BPFO, @BPFI, @FTF, @BSF)";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@BearNB", bearing.BearNB ?? "");
                            cmd.Parameters.AddWithValue("@NB", bearing.NB ?? "");
                            cmd.Parameters.AddWithValue("@BD", bearing.BD ?? "");
                            cmd.Parameters.AddWithValue("@PD", bearing.PD ?? "");
                            cmd.Parameters.AddWithValue("@PHI", bearing.PHI ?? "");
                            cmd.Parameters.AddWithValue("@BPFO", bearing.BPFO ?? "");
                            cmd.Parameters.AddWithValue("@BPFI", bearing.BPFI ?? "");
                            cmd.Parameters.AddWithValue("@FTF", bearing.FTF ?? "");
                            cmd.Parameters.AddWithValue("@BSF", bearing.BSF ?? "");
                            cmd.CommandTimeout = 30;
                            int rows = cmd.ExecuteNonQuery();
                            if (rows > 0)
                            {
                                Logger.Info($"Bearing '{bearing.BearNB}' added successfully");
                                return GetLastInsertedId(conn);
                            }
                            throw new Exception("Failed to insert bearing");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"AddBearingAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<bool> UpdateBearingAsync(BearingModel bearing)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (bearing == null)
                        throw new ArgumentNullException(nameof(bearing));

                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info($"Updating bearing ID: {bearing.ID}");
                        string sql = $@"UPDATE {BEARINGS_TABLE} 
                            SET BearNB=@BearNB, NB=@NB, BD=@BD, PD=@PD, 
                                PHI=@PHI, BPFO=@BPFO, BPFI=@BPFI, FTF=@FTF, BSF=@BSF 
                            WHERE ID=@ID";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ID", bearing.ID);
                            cmd.Parameters.AddWithValue("@BearNB", bearing.BearNB ?? "");
                            cmd.Parameters.AddWithValue("@NB", bearing.NB ?? "");
                            cmd.Parameters.AddWithValue("@BD", bearing.BD ?? "");
                            cmd.Parameters.AddWithValue("@PD", bearing.PD ?? "");
                            cmd.Parameters.AddWithValue("@PHI", bearing.PHI ?? "");
                            cmd.Parameters.AddWithValue("@BPFO", bearing.BPFO ?? "");
                            cmd.Parameters.AddWithValue("@BPFI", bearing.BPFI ?? "");
                            cmd.Parameters.AddWithValue("@FTF", bearing.FTF ?? "");
                            cmd.Parameters.AddWithValue("@BSF", bearing.BSF ?? "");
                            cmd.CommandTimeout = 30;
                            int rows = cmd.ExecuteNonQuery();
                            if (rows > 0)
                            {
                                Logger.Info($"Bearing ID {bearing.ID} updated successfully");
                                return true;
                            }
                            Logger.Warn($"No bearing found with ID {bearing.ID} to update");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"UpdateBearingAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<bool> DeleteBearingAsync(int id)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info($"Deleting bearing ID: {id}");
                        string sql = $"DELETE FROM {BEARINGS_TABLE} WHERE ID = @ID";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@ID", id);
                            cmd.CommandTimeout = 30;
                            int rows = cmd.ExecuteNonQuery();
                            if (rows > 0)
                            {
                                Logger.Info($"Bearing ID {id} deleted successfully");
                                return true;
                            }
                            Logger.Warn($"No bearing found with ID {id} to delete");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"DeleteBearingAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<List<BearingModel>> SearchBearingsAsync(string searchTerm)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var bearings = new List<BearingModel>();
                    if (string.IsNullOrWhiteSpace(searchTerm))
                        return bearings;

                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info($"Searching bearings with term: {searchTerm}");
                        string sql = $"SELECT ID, BearNB, NB, BD, PD, PHI, BPFO, BPFI, FTF, BSF FROM {BEARINGS_TABLE} WHERE BearNB LIKE @SearchTerm";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");
                            cmd.CommandTimeout = 30;
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                    bearings.Add(ReadBearing(reader));
                            }
                        }
                    }
                    Logger.Info($"Found {bearings.Count} bearings matching: {searchTerm}");
                    return bearings;
                }
                catch (Exception ex)
                {
                    Logger.Error($"SearchBearingsAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<int> GetBearingCountAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        string sql = $"SELECT COUNT(*) FROM {BEARINGS_TABLE}";
                        using (var cmd = new OleDbCommand(sql, conn))
                        {
                            cmd.CommandTimeout = 30;
                            int count = (int)cmd.ExecuteScalar();
                            Logger.Info($"Total bearings in database: {count}");
                            return count;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetBearingCountAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        public async Task<bool> TestConnectionAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var conn = new OleDbConnection(_connectionString))
                    {
                        conn.Open();
                        Logger.Info("Database connection test successful");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Database connection test failed: {ex.Message}", ex);
                    return false;
                }
            });
        }

        public async Task<Dictionary<string, object>> GetDatabaseStatsAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var stats = new Dictionary<string, object>();
                    int count = await GetBearingCountAsync();
                    bool isConnected = await TestConnectionAsync();
                    stats.Add("TotalBearings", count);
                    stats.Add("IsConnected", isConnected);
                    stats.Add("ConnectionString", MaskConnectionString(_connectionString));
                    stats.Add("LastQueried", DateTime.Now);
                    Logger.Info("Database statistics retrieved");
                    return stats;
                }
                catch (Exception ex)
                {
                    Logger.Error($"GetDatabaseStatsAsync failed: {ex.Message}", ex);
                    throw;
                }
            });
        }

        private BearingModel ReadBearing(OleDbDataReader reader)
        {
            return new BearingModel
            {
                ID = Convert.ToInt32(reader["ID"] ?? 0),
                BearNB = reader["BearNB"]?.ToString() ?? "",
                NB = reader["NB"]?.ToString() ?? "",
                BD = reader["BD"]?.ToString() ?? "",
                PD = reader["PD"]?.ToString() ?? "",
                PHI = reader["PHI"]?.ToString() ?? "",
                BPFO = reader["BPFO"]?.ToString() ?? "",
                BPFI = reader["BPFI"]?.ToString() ?? "",
                FTF = reader["FTF"]?.ToString() ?? "",
                BSF = reader["BSF"]?.ToString() ?? ""
            };
        }

        private int GetLastInsertedId(OleDbConnection connection)
        {
            try
            {
                using (var cmd = new OleDbCommand("SELECT @@IDENTITY", connection))
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"GetLastInsertedId failed: {ex.Message}", ex);
                return -1;
            }
        }

        private string MaskConnectionString(string cs)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(cs, @"(Data Source=)([^;]*)", "$1***");
            }
            catch { return "***"; }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    Logger.Info("DatabaseService disposed");
                _disposed = true;
            }
        }

        ~DatabaseService() { Dispose(false); }
    }
}