using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace AeroAI.Atc.Vectoring;

public sealed class NavDataWaypointResolver : IWaypointResolver
{
	private readonly string _connectionString;

	private readonly Dictionary<string, WaypointPosition> _cache = new Dictionary<string, WaypointPosition>(StringComparer.OrdinalIgnoreCase);

	public NavDataWaypointResolver(string connectionString)
	{
		_connectionString = connectionString;
	}

	public WaypointPosition? GetWaypointPosition(string waypointIdentifier)
	{
		if (string.IsNullOrWhiteSpace(waypointIdentifier))
		{
			return null;
		}
		if (_cache.TryGetValue(waypointIdentifier, out WaypointPosition value))
		{
			return value;
		}
		WaypointPosition? waypointPosition = TryFindInWaypointsTable(waypointIdentifier) ?? TryFindInVorsTable(waypointIdentifier) ?? TryFindInNdbsTable(waypointIdentifier) ?? TryFindInPathpointsTable(waypointIdentifier);
		if (waypointPosition != null)
		{
			_cache[waypointIdentifier] = waypointPosition;
		}
		return waypointPosition;
	}

	private WaypointPosition? TryFindInWaypointsTable(string identifier)
	{
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                SELECT waypoint_latitude, waypoint_longitude\r\n                FROM tbl_waypoints\r\n                WHERE waypoint_identifier = $ident\r\n                LIMIT 1;";
			sqliteCommand.Parameters.AddWithValue("$ident", identifier.ToUpperInvariant());
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				double num = (sqliteDataReader.IsDBNull(0) ? 0.0 : sqliteDataReader.GetDouble(0));
				double num2 = (sqliteDataReader.IsDBNull(1) ? 0.0 : sqliteDataReader.GetDouble(1));
				if (num != 0.0 || num2 != 0.0)
				{
					return new WaypointPosition
					{
						Latitude = num,
						Longitude = num2
					};
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private WaypointPosition? TryFindInVorsTable(string identifier)
	{
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                SELECT vor_latitude, vor_longitude\r\n                FROM tbl_vors\r\n                WHERE vor_identifier = $ident\r\n                LIMIT 1;";
			sqliteCommand.Parameters.AddWithValue("$ident", identifier.ToUpperInvariant());
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				double num = (sqliteDataReader.IsDBNull(0) ? 0.0 : sqliteDataReader.GetDouble(0));
				double num2 = (sqliteDataReader.IsDBNull(1) ? 0.0 : sqliteDataReader.GetDouble(1));
				if (num != 0.0 || num2 != 0.0)
				{
					return new WaypointPosition
					{
						Latitude = num,
						Longitude = num2
					};
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private WaypointPosition? TryFindInNdbsTable(string identifier)
	{
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                SELECT ndb_latitude, ndb_longitude\r\n                FROM tbl_ndbs\r\n                WHERE ndb_identifier = $ident\r\n                LIMIT 1;";
			sqliteCommand.Parameters.AddWithValue("$ident", identifier.ToUpperInvariant());
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				double num = (sqliteDataReader.IsDBNull(0) ? 0.0 : sqliteDataReader.GetDouble(0));
				double num2 = (sqliteDataReader.IsDBNull(1) ? 0.0 : sqliteDataReader.GetDouble(1));
				if (num != 0.0 || num2 != 0.0)
				{
					return new WaypointPosition
					{
						Latitude = num,
						Longitude = num2
					};
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private WaypointPosition? TryFindInPathpointsTable(string identifier)
	{
		try
		{
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = sqliteConnection.CreateCommand();
			sqliteCommand.CommandText = "\r\n                SELECT waypoint_latitude, waypoint_longitude\r\n                FROM tbl_pathpoints\r\n                WHERE waypoint_identifier = $ident\r\n                LIMIT 1;";
			sqliteCommand.Parameters.AddWithValue("$ident", identifier.ToUpperInvariant());
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			if (sqliteDataReader.Read())
			{
				double num = (sqliteDataReader.IsDBNull(0) ? 0.0 : sqliteDataReader.GetDouble(0));
				double num2 = (sqliteDataReader.IsDBNull(1) ? 0.0 : sqliteDataReader.GetDouble(1));
				if (num != 0.0 || num2 != 0.0)
				{
					return new WaypointPosition
					{
						Latitude = num,
						Longitude = num2
					};
				}
			}
		}
		catch
		{
		}
		return null;
	}
}
