using AtcNavDataDemo.Models;
using Microsoft.Data.Sqlite;

namespace AtcNavDataDemo.Data;

/// <summary>
/// Reads PMDG navdata from the SQLite database.
/// </summary>
public class NavDataReader
{
    private readonly string _connectionString;

    public NavDataReader(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path must be provided.", nameof(databasePath));

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };

        _connectionString = builder.ToString();
    }

    /// <summary>
    /// Debug helper: list all table names in the database.
    /// </summary>
    public List<string> GetTableNames()
    {
        var tables = new List<string>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";

        using var reader = command.ExecuteReader();
        var ordName = reader.GetOrdinal("name");

        while (reader.Read())
        {
            var name = reader.IsDBNull(ordName) ? string.Empty : reader.GetString(ordName);
            if (!string.IsNullOrWhiteSpace(name))
                tables.Add(name);
        }

        return tables;
    }

    /// <summary>
    /// Debug helper: list column names for a given table.
    /// </summary>
    public List<string> GetTableColumns(string tableName)
    {
        var columns = new List<string>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        var ordName = reader.GetOrdinal("name");

        while (reader.Read())
        {
            var name = reader.IsDBNull(ordName) ? string.Empty : reader.GetString(ordName);
            if (!string.IsNullOrWhiteSpace(name))
                columns.Add(name);
        }

        return columns;
    }

    /// <summary>
    /// Returns distinct (procedure_identifier, route_type, transition_identifier) for the given airport,
    /// based on tbl_iaps (instrument approach procedures).
    /// </summary>
    public List<ApproachSummary> ListApproachSummaries(string icao)
    {
        var results = new List<ApproachSummary>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        const string sql = @"
            SELECT DISTINCT
                procedure_identifier,
                route_type,
                transition_identifier
            FROM tbl_iaps
            WHERE airport_identifier = $icao
            ORDER BY procedure_identifier, route_type, transition_identifier;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$icao", icao);

        using var reader = command.ExecuteReader();

        var ordProc = reader.GetOrdinal("procedure_identifier");
        var ordRoute = reader.GetOrdinal("route_type");
        var ordTrans = reader.GetOrdinal("transition_identifier");

        while (reader.Read())
        {
            var procId = reader.IsDBNull(ordProc) ? string.Empty : reader.GetString(ordProc);
            var routeType = reader.IsDBNull(ordRoute) ? string.Empty : reader.GetString(ordRoute);
            var transitionId = reader.IsDBNull(ordTrans) ? string.Empty : reader.GetString(ordTrans);

            results.Add(new ApproachSummary(procId, routeType, transitionId));
        }

        return results;
    }

    /// <summary>
    /// Loads an ordered procedure (ORDER BY seqno) for the given identifiers.
    /// </summary>
    public Procedure LoadProcedure(string icao, string procedureId, string routeType, string transitionId)
    {
        var legs = new List<ProcedureLeg>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        const string sql = @"
            SELECT
                airport_identifier,
                procedure_identifier,
                route_type,
                transition_identifier,
                seqno,
                waypoint_identifier,
                waypoint_latitude,
                waypoint_longitude,
                path_termination,
                altitude_description,
                altitude1,
                altitude2,
                speed_limit_description,
                speed_limit
            FROM tbl_iaps
            WHERE airport_identifier = $icao
              AND procedure_identifier = $proc
              AND route_type = $route
              AND transition_identifier = $trans
            ORDER BY seqno;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$icao", icao);
        command.Parameters.AddWithValue("$proc", procedureId);
        command.Parameters.AddWithValue("$route", routeType);
        command.Parameters.AddWithValue("$trans", transitionId);

        using var reader = command.ExecuteReader();

        var ordAirport = reader.GetOrdinal("airport_identifier");
        var ordProc = reader.GetOrdinal("procedure_identifier");
        var ordRoute = reader.GetOrdinal("route_type");
        var ordTrans = reader.GetOrdinal("transition_identifier");
        var ordSeq = reader.GetOrdinal("seqno");
        var ordWpId = reader.GetOrdinal("waypoint_identifier");
        var ordLat = reader.GetOrdinal("waypoint_latitude");
        var ordLon = reader.GetOrdinal("waypoint_longitude");
        var ordPathTerm = reader.GetOrdinal("path_termination");
        var ordAltDesc = reader.GetOrdinal("altitude_description");
        var ordAlt1 = reader.GetOrdinal("altitude1");
        var ordAlt2 = reader.GetOrdinal("altitude2");
        var ordSpeedDesc = reader.GetOrdinal("speed_limit_description");
        var ordSpeed = reader.GetOrdinal("speed_limit");

        while (reader.Read())
        {
            string airportIdentifier = reader.IsDBNull(ordAirport) ? string.Empty : reader.GetString(ordAirport);
            string procIdentifier = reader.IsDBNull(ordProc) ? string.Empty : reader.GetString(ordProc);
            string rType = reader.IsDBNull(ordRoute) ? string.Empty : reader.GetString(ordRoute);
            string transId = reader.IsDBNull(ordTrans) ? string.Empty : reader.GetString(ordTrans);

            int seqno = reader.IsDBNull(ordSeq) ? 0 : reader.GetInt32(ordSeq);
            string wpId = reader.IsDBNull(ordWpId) ? string.Empty : reader.GetString(ordWpId);
            double lat = reader.IsDBNull(ordLat) ? 0.0 : reader.GetDouble(ordLat);
            double lon = reader.IsDBNull(ordLon) ? 0.0 : reader.GetDouble(ordLon);

            string pathTerm = reader.IsDBNull(ordPathTerm) ? string.Empty : reader.GetString(ordPathTerm);
            string altDesc = reader.IsDBNull(ordAltDesc) ? string.Empty : reader.GetString(ordAltDesc);
            int alt1 = reader.IsDBNull(ordAlt1) ? 0 : reader.GetInt32(ordAlt1);
            int alt2 = reader.IsDBNull(ordAlt2) ? 0 : reader.GetInt32(ordAlt2);

            string speedDesc = reader.IsDBNull(ordSpeedDesc) ? string.Empty : reader.GetString(ordSpeedDesc);
            int speed = reader.IsDBNull(ordSpeed) ? 0 : reader.GetInt32(ordSpeed);

            var leg = new ProcedureLeg(
                airportIdentifier,
                procIdentifier,
                rType,
                transId,
                seqno,
                wpId,
                lat,
                lon,
                pathTerm,
                altDesc,
                alt1,
                alt2,
                speedDesc,
                speed);

            legs.Add(leg);
        }

        return new Procedure(icao, procedureId, routeType, transitionId, legs);
    }
}
