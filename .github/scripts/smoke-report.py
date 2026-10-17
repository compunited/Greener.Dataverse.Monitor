"""Exercise PDF and chart generation with synthetic data; no Dataverse connection."""

import csv
import subprocess
import sys
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path


report = Path(sys.argv[1]).resolve()
command = ["dotnet", str(report)] if report.suffix == ".dll" else [str(report)]
with tempfile.TemporaryDirectory(prefix="dataverse-report-smoke-") as temporary:
    directory = Path(temporary)
    input_file = directory / "synthetic.csv"
    output_file = directory / "report.pdf"
    settings_file = directory / "appsettings.json"
    settings_file.write_text("{}", encoding="utf-8")
    with input_file.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(["timestamp_utc", "query_name", "event_type", "method",
                         "url", "status", "ttfb_ms", "total_ms", "error"])
        start = datetime(2026, 1, 1, tzinfo=timezone.utc)
        for sample in range(180):
            writer.writerow([(start + timedelta(minutes=sample)).isoformat(),
                             "synthetic-query", "result", "GET",
                             "https://example.invalid/api/data/v9.0/accounts",
                             200, 50 + sample % 7, 100 + sample % 17, ""])
    subprocess.run(command + ["--input", str(input_file), "--output", str(output_file),
                              "--appsettings", str(settings_file)], check=True, cwd=directory)
    if output_file.stat().st_size < 1000 or not output_file.read_bytes().startswith(b"%PDF-"):
        raise RuntimeError("Report generation did not produce a valid PDF header")
    charts = list(directory.glob("charts_*/*.png"))
    if len(charts) < 3 or any(chart.stat().st_size < 100 for chart in charts):
        raise RuntimeError("Expected timeline, histogram and minute profile charts")
    print(f"Synthetic report smoke test passed: PDF and {len(charts)} charts.")
