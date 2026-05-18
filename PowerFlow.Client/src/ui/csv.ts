// Tiny CSV serializer + browser download. Quotes any cell containing a
// quote, comma, or newline. No streaming — case300's biggest tab is ~411
// rows × ~10 cols, well under what a single string can hold.

export type CsvCell = string | number | boolean | null | undefined;

export function downloadCsv(filename: string, headers: string[], rows: CsvCell[][]): void {
  const lines = [headers.map(escape).join(',')];
  for (const row of rows) {
    lines.push(row.map((c) => escape(stringify(c))).join(','));
  }
  const blob = new Blob([lines.join('\n')], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

function stringify(c: CsvCell): string {
  if (c === null || c === undefined) return '';
  return typeof c === 'string' ? c : String(c);
}

function escape(s: string): string {
  if (/[",\n]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
  return s;
}
