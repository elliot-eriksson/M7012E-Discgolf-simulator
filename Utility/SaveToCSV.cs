using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class SaveToCSV: MonoBehaviour{
	    public void SaveToCSVFile(List<Vector3> logDataPoints){
			System.Text.StringBuilder csv = new StringBuilder();
			csv.AppendLine("x;y;z");
			foreach (Vector3 point in logDataPoints)
			{
				csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2}", point.x, point.y, point.z));
			}

			// Save the CSV file.
			string filePath = Path.Combine(Application.persistentDataPath, "datapoints.csv");
			File.WriteAllText(filePath, csv.ToString());
			Debug.Log($"Datapoints saved to: {filePath}");

    }
}