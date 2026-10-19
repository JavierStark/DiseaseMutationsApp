"""Reads Diana's GenBank and SBOL 3 exports with the reference libraries and prints what they understood as JSON.

Used by ExternalValidatorTests, which compares the output with the expected layout. Exit code 3 means a library is not
installed (the test then skips itself). Libraries used: Biopython (SeqIO) and pySBOL3 (sbol3).
Usage: python -I validate_export.py <file.gb> <file.xml>
"""
import json
import sys

try:
    from Bio import SeqIO
    import Bio
    import sbol3
except ImportError as e:  # pragma: no cover
    print(f"missing module: {e}", file=sys.stderr)
    sys.exit(3)

gb_path, sbol_path = sys.argv[1], sys.argv[2]

records = []
for rec in SeqIO.parse(gb_path, "genbank"):
    records.append({
        "name": rec.name,
        "length": len(rec.seq),
        "sequence": str(rec.seq).upper(),
        "topology": rec.annotations.get("topology"),
        "division": rec.annotations.get("data_file_division"),
        "organism": rec.annotations.get("organism"),
        "features": [
            {
                "type": f.type,
                "start": int(f.location.start) + 1,
                "end": int(f.location.end),
                "strand": f.location.strand,
                "qualifiers": {k: v for k, v in f.qualifiers.items()},
            }
            for f in rec.features
        ],
    })

# Round trip: what Biopython writes, Biopython must read back unchanged (sequence and feature locations).
import io
buf = io.StringIO()
SeqIO.write(list(SeqIO.parse(gb_path, "genbank")), buf, "genbank")
again = list(SeqIO.parse(io.StringIO(buf.getvalue()), "genbank"))
roundtrip_ok = (
    len(again) == len(records)
    and all(str(a.seq).upper() == r["sequence"] for a, r in zip(again, records))
    and all([(int(f.location.start), int(f.location.end)) for f in a.features]
            == [(f["start"] - 1, f["end"]) for f in r["features"]] for a, r in zip(again, records))
)

doc = sbol3.Document()
doc.read(sbol_path)
report = doc.validate()
components = []
for c in doc.find_all(lambda o: isinstance(o, sbol3.Component)):
    seq = c.sequences[0].lookup() if c.sequences else None
    components.append({
        "displayId": c.display_id,
        "identity": c.identity,
        "namespace": c.namespace,
        "types": sorted(c.types),
        "roles": sorted(c.roles),
        "sequence": seq.elements.upper() if seq is not None else None,
        "encoding": seq.encoding if seq is not None else None,
        "features": [
            {
                "name": f.name,
                "roles": sorted(f.roles),
                "ranges": [
                    {"start": loc.start, "end": loc.end, "orientation": loc.orientation}
                    for loc in f.locations
                ],
            }
            for f in c.features
        ],
    })

# SBOL round trip through pySBOL3's own writer.
import tempfile, os
with tempfile.TemporaryDirectory() as d:
    out = os.path.join(d, "again.nt")
    doc.write(out, sbol3.SORTED_NTRIPLES)
    doc2 = sbol3.Document()
    doc2.read(out)
    sbol_roundtrip_ok = len(list(doc2.objects)) == len(list(doc.objects))

print(json.dumps({
    "versions": {"biopython": Bio.__version__, "sbol3": getattr(sbol3, "__version__", "unknown")},
    "genbank": records,
    "genbankRoundTripOk": roundtrip_ok,
    "sbolErrors": [str(e) for e in report.errors],
    "sbolWarnings": [str(w) for w in report.warnings],
    "sbolComponents": sorted(components, key=lambda c: c["displayId"]),
    "sbolRoundTripOk": sbol_roundtrip_ok,
}))
