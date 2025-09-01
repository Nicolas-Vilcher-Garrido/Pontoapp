using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Linq;

// ========================= HTML/JS =========================
const string HtmlPage = """
<!doctype html><meta charset="utf-8">
<title>Ponto Rápido</title>
<style>
  body{font-family:system-ui,Segoe UI,Roboto,Arial,sans-serif;max-width:860px;margin:40px auto;padding:0 16px}
  h1{margin:0 0 8px}
  .row{display:flex;gap:8px;flex-wrap:wrap}
  button{padding:10px 14px;border:0;border-radius:8px;box-shadow:0 1px 2px rgba(0,0,0,.15);cursor:pointer}
  .ok{background:#0a7; color:#fff}
  .warn{background:#06c; color:#fff}
  input,select{padding:8px;border:1px solid #ddd;border-radius:8px}
  table{width:100%; border-collapse:collapse; margin-top:16px}
  th,td{border-bottom:1px solid #eee; padding:8px; text-align:left}
  .muted{color:#666}
  .chip{display:inline-block;padding:.2rem .5rem;border-radius:999px;background:#eee}
</style>

<h1>Registro de Ponto</h1>
<p class="muted">Bata o ponto agora (Entrada/Saída) ou cadastre/edite manualmente.</p>

<div class="row">
  <button type="button" class="ok"  onclick="punch('entrada')">Bater agora — Entrada</button>
  <button type="button" class="warn" onclick="punch('saida')">Bater agora — Saída</button>
</div>

<h3 style="margin-top:24px">Cadastrar/Editar manual</h3>
<div class="row">
  <input type="datetime-local" id="ts">
  <select id="kind">
    <option value="entrada">Entrada</option>
    <option value="saida">Saída</option>
  </select>
  <input id="note" placeholder="Observação (opcional)">
  <button type="button" onclick="addManual()">Salvar</button>
  <span id="editing" class="chip" style="display:none"></span>
</div>

<h3 style="margin-top:24px">Registros de <input type="date" id="datePick" onchange="load()" /></h3>
<table id="tbl">
  <thead>
    <tr><th>Hora</th><th>Tipo</th><th>Obs.</th><th>Ações</th></tr>
  </thead>
  <tbody></tbody>
</table>

<script>
const $ = s => document.querySelector(s);
const fmt = d => new Date(d).toLocaleString();
function todayISO(){ const d=new Date(); d.setHours(0,0,0,0); return d.toISOString().slice(0,10); }
function esc(s){ return (s??"").replaceAll("&","&amp;").replaceAll("<","&lt;").replaceAll(">","&gt;").replaceAll("\"","&quot;").replaceAll("'","&#39;"); }

// ISO com offset local (evita mudar de dia ao salvar)
function localIsoWithOffset(tsLocal){
  const d = new Date(tsLocal);
  const pad = n=> String(n).padStart(2,'0');
  const offMin = -d.getTimezoneOffset();
  const sign = offMin>=0 ? '+' : '-';
  const hh = pad(Math.floor(Math.abs(offMin)/60));
  const mm = pad(Math.abs(offMin)%60);
  return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}:00${sign}${hh}:${mm}`;
}

async function load(){
  const day = $('#datePick').value || todayISO();
  const res = await fetch('/api/entries?date='+day,{cache:'no-store'});
  const data = await res.json();
  const tb = $('#tbl tbody'); tb.innerHTML = '';
  data.forEach(e=>{
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${fmt(e.timestamp)}</td>
      <td>${e.kind}</td>
      <td>${esc(e.note)}</td>
      <td>
        <button type="button" onclick='edit("${e.id}","${e.timestamp}","${e.kind}","${esc(e.note)}")'>Editar</button>
        <button type="button" onclick='del("${e.id}","${e.timestamp}","${e.kind}","${esc(e.note)}")'>Apagar</button>
      </td>`;
    tb.appendChild(tr);
  });
}

async function punch(kind){
  const note = prompt('Observação (opcional):') ?? null;
  await fetch('/api/punch',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({kind, note})});
  load();
}

let editingId = null;
function edit(id, ts, kind, note){
  editingId = id;
  $('#editing').style.display='inline-block';
  $('#editing').textContent = 'Editando '+id.slice(0,8)+'...';
  const t = new Date(ts);
  const pad = n=> String(n).padStart(2,'0');
  const local = t.getFullYear()+'-'+pad(t.getMonth()+1)+'-'+pad(t.getDate())+'T'+pad(t.getHours())+':'+pad(t.getMinutes());
  $('#ts').value = local;
  $('#kind').value = kind;
  $('#note').value = note ?? '';
}

async function addManual(){
  const ts = $('#ts').value; if(!ts){ alert('Informe data/hora'); return; }
  const kind = $('#kind').value;
  const note = $('#note').value || null;
  const payload = { timestamp: localIsoWithOffset(ts), kind, note };

  if(editingId){
    const r = await fetch('/api/entries/'+encodeURIComponent(editingId),{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
    if(!r.ok){ alert('Falha ao editar: '+await r.text()); }
  } else {
    const r = await fetch('/api/entries',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
    if(!r.ok){ alert('Falha ao criar: '+await r.text()); }
  }
  editingId=null; $('#editing').style.display='none'; $('#ts').value=''; $('#note').value='';
  load();
}

// Apaga por ID; se o servidor não achar, tenta por (timestamp+kind+note)
async function del(id, ts, kind, note){
  if(!confirm('Apagar este registro?')) return;

  // 1) DELETE /api/entries/{id}
  let r = await fetch('/api/entries/'+encodeURIComponent(id),{method:'DELETE'});
  if(r.ok){ load(); return; }

  // 2) Fallback robusto
  const payload = { id, timestamp: ts, kind, note };
  r = await fetch('/api/entries/remove',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
  if(!r.ok){ alert('Falha ao apagar: '+await r.text()); }
  load();
}

document.addEventListener('DOMContentLoaded', ()=>{
  $('#datePick').value = todayISO();
  load();
});
</script>
""";

// ========================= APP =========================
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ---------- Store (JSON em disco) ----------
var dataDir  = Path.Combine(AppContext.BaseDirectory, "data");
var dataFile = Path.Combine(dataDir, "pontos.json");
Directory.CreateDirectory(dataDir);

// JSON tolerante (camelCase) + case-insensitive
var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true
};

var store = new PontoStore(dataFile, jsonOpts);

// ---------- Util de nível superior ----------
static string? NormalizeKind(string? k)
{
    if (string.IsNullOrWhiteSpace(k)) return null;
    k = k.Trim().ToLowerInvariant();
    return k switch
    {
        "entrada" or "in"  or "ent" => "entrada",
        "saida"   or "out" or "sai" => "saida",
        _ => null
    };
}

static string EscapeCsv(string? s)
{
    if (string.IsNullOrEmpty(s)) return "";
    if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    return s;
}

// ---------- Endpoints ----------
app.MapGet("/", () => Results.Content(HtmlPage, "text/html"));

app.MapGet("/api/entries", async (string? date) =>
{
    DateOnly target = DateOnly.FromDateTime(DateTime.Now);
    if (!string.IsNullOrWhiteSpace(date) && DateOnly.TryParse(date, out var d)) target = d;
    var list = await store.GetByDateAsync(target);
    return Results.Ok(list.OrderBy(e => e.Timestamp));
});

app.MapPost("/api/punch", async (PunchNowRequest req) =>
{
    var kind = NormalizeKind(req.Kind);
    if (kind is null) return Results.BadRequest(new { error = "Kind inválido. Use 'entrada' ou 'saida'." });
    var entry = new PontoEntry(Guid.NewGuid(), DateTimeOffset.Now, kind, req.Note);
    await store.UpsertAsync(entry);
    return Results.Ok(entry);
});

app.MapPost("/api/entries", async (ManualEntryRequest req) =>
{
    var kind = NormalizeKind(req.Kind);
    if (kind is null) return Results.BadRequest(new { error = "Kind inválido. Use 'entrada' ou 'saida'." });
    var entry = new PontoEntry(Guid.NewGuid(), req.Timestamp, kind, req.Note);
    await store.UpsertAsync(entry);
    return Results.Ok(entry);
});

app.MapPut("/api/entries/{id}", async (string id, ManualEntryRequest req) =>
{
    var kind = NormalizeKind(req.Kind);
    if (kind is null) return Results.BadRequest(new { error = "Kind inválido. Use 'entrada' ou 'saida'." });
    if (!Guid.TryParse(id, out var gid)) return Results.BadRequest(new { error = "Id inválido" });

    var existing = await store.GetAsync(gid);
    if (existing is null) return Results.NotFound(new { error = "Registro não encontrado" });

    var updated = existing with { Timestamp = req.Timestamp, Kind = kind, Note = req.Note };
    await store.UpsertAsync(updated);
    return Results.Ok(updated);
});

// DELETE tolerante por texto de ID (sem exigir Guid)
app.MapDelete("/api/entries/{id}", async (string idRaw) =>
{
    Console.WriteLine($"[DELETE] recebido idRaw='{idRaw}'");
    var ok = await store.DeleteByTextAsync(idRaw);
    Console.WriteLine($"[DELETE] resultado: {(ok ? "REMOVIDO" : "NÃO ENCONTRADO")}");
    return ok ? Results.NoContent()
              : Results.NotFound(new { error = "Registro não encontrado", id = idRaw });
});

// Fallback: apaga por (timestamp + kind + note)
app.MapPost("/api/entries/remove", async (RemoveRequest req) =>
{
    Console.WriteLine($"[REMOVE] body id='{req.Id}', ts='{req.Timestamp:O}', kind='{req.Kind}', note='{req.Note}'");

    if (!string.IsNullOrWhiteSpace(req.Id))
    {
        if (await store.DeleteByTextAsync(req.Id!))
            return Results.NoContent();
    }

    if (req.Timestamp.HasValue && !string.IsNullOrWhiteSpace(req.Kind))
    {
        var ok = await store.DeleteByCompositeAsync(req.Timestamp.Value, req.Kind!, req.Note);
        if (ok) return Results.NoContent();
    }

    return Results.NotFound(new { error = "Registro não encontrado para remoção (ID e/ou combinação não bateram)." });
});

app.MapGet("/api/export.csv", async () =>
{
    var all = await store.GetAllAsync();
    var csv = "Id,Timestamp,Kind,Note\r\n" +
              string.Join("\r\n", all.OrderBy(e => e.Timestamp).Select(e =>
                $"{e.Id},{e.Timestamp:o},{e.Kind},{EscapeCsv(e.Note)}"));
    return Results.Text(csv, "text/csv");
});

app.Run();

// ========================= Tipos =========================
public record PontoEntry(Guid Id, DateTimeOffset Timestamp, string Kind, string? Note);
public record PunchNowRequest(string Kind, string? Note);
public record ManualEntryRequest(DateTimeOffset Timestamp, string Kind, string? Note);
public record RemoveRequest(string? Id, DateTimeOffset? Timestamp, string? Kind, string? Note);

// ========================= Store =========================
class PontoStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _opts;
    private readonly SemaphoreSlim _lock = new(1,1);

    public PontoStore(string path, JsonSerializerOptions opts)
    {
        _path = path;
        _opts = opts;
    }

    private static string? NormalizeKindInternal(string? k)
    {
        if (string.IsNullOrWhiteSpace(k)) return null;
        k = k.Trim().ToLowerInvariant();
        return k switch
        {
            "entrada" or "in"  or "ent" => "entrada",
            "saida"   or "out" or "sai" => "saida",
            _ => null
        };
    }

    private async Task SaveListAsync(List<PontoEntry> list)
    {
        // overwrite com exclusividade; garante que nenhum stream de leitura fique aberto
        using var fsW = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(fsW, list, _opts);
    }

    public async Task<IReadOnlyList<PontoEntry>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return Array.Empty<PontoEntry>();
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var list = await JsonSerializer.DeserializeAsync<List<PontoEntry>>(fs, _opts) ?? new List<PontoEntry>();
                return list;
            }
        }
        finally { _lock.Release(); }
    }

    public async Task<PontoEntry?> GetAsync(Guid id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(x => x.Id == id);
    }

    public async Task<List<PontoEntry>> GetByDateAsync(DateOnly date)
    {
        var all = await GetAllAsync();
        return all
            .Where(e => DateOnly.FromDateTime(e.Timestamp.ToLocalTime().Date) == date)
            .ToList();
    }

    public async Task UpsertAsync(PontoEntry entry)
    {
        await _lock.WaitAsync();
        try
        {
            var list = new List<PontoEntry>();
            if (File.Exists(_path))
            {
                using (var fsR = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    list = await JsonSerializer.DeserializeAsync<List<PontoEntry>>(fsR, _opts) ?? new List<PontoEntry>();
                }
            }

            var idx = list.FindIndex(x => x.Id == entry.Id);
            if (idx >= 0) list[idx] = entry; else list.Add(entry);

            await SaveListAsync(list);
        }
        finally { _lock.Release(); }
    }

    // --- Remoção tolerante a qualquer texto de ID ---
    public async Task<bool> DeleteByTextAsync(string idText)
    {
        if (string.IsNullOrWhiteSpace(idText)) return false;
        idText = idText.Trim();

        static string San(string s) =>
            Regex.Replace(s, "[{}()\\-\\s]", "").ToLowerInvariant();

        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return false;

            List<PontoEntry> list;
            using (var fsR = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                list = await JsonSerializer.DeserializeAsync<List<PontoEntry>>(fsR, _opts) ?? new List<PontoEntry>();
            }

            bool removed = false;

            // 1) Guid direto
            if (Guid.TryParse(idText, out var gid))
                removed = list.RemoveAll(x => x.Id == gid) > 0;

            // 2) Equivalência textual
            if (!removed)
            {
                var want = San(idText);
                var idx = list.FindIndex(x =>
                {
                    var d = San(x.Id.ToString("D"));
                    var n = San(x.Id.ToString("N"));
                    var b = San(x.Id.ToString("B"));
                    var p = San(x.Id.ToString("P"));
                    return want == d || want == n || want == b || want == p;
                });
                if (idx >= 0)
                {
                    Console.WriteLine($"[DELETE] match textual idx={idx} id={list[idx].Id}");
                    list.RemoveAt(idx);
                    removed = true;
                }
            }

            if (!removed) return false;

            await SaveListAsync(list);
            return true;
        }
        finally { _lock.Release(); }
    }

    // --- Remoção por (timestamp + kind + note) ---
    public async Task<bool> DeleteByCompositeAsync(DateTimeOffset ts, string kind, string? note)
    {
        var nkind = NormalizeKindInternal(kind);
        if (nkind is null) return false;

        static bool SameTs(DateTimeOffset a, DateTimeOffset b)
            => Math.Abs((a - b).TotalSeconds) <= 2;

        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_path)) return false;

            List<PontoEntry> list;
            using (var fsR = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                list = await JsonSerializer.DeserializeAsync<List<PontoEntry>>(fsR, _opts) ?? new List<PontoEntry>();
            }

            var idx = list.FindIndex(x =>
                SameTs(x.Timestamp, ts) &&
                string.Equals(x.Kind, nkind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Note ?? "", note ?? "", StringComparison.Ordinal));

            if (idx < 0) return false;

            Console.WriteLine($"[REMOVE] composite idx={idx} id={list[idx].Id} ts={list[idx].Timestamp:o} kind={list[idx].Kind} note={list[idx].Note}");
            list.RemoveAt(idx);

            await SaveListAsync(list);
            return true;
        }
        finally { _lock.Release(); }
    }
}
