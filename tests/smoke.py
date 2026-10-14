"""Integration smoke checks. Start DBViewer before running python3 tests/smoke.py."""
import json
import os
import sqlite3
import tempfile
import urllib.request
import urllib.error

BASE = os.environ.get('DBVIEWER_URL', 'http://127.0.0.1:5080')

def call(path, data=None, origin=None):
    headers = {'Content-Type': 'application/json'}
    if origin:
        headers['Origin'] = origin
    req = urllib.request.Request(BASE + path, data=json.dumps(data).encode() if data is not None else None, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=35) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b'{}')

status, demo = call('/api/demo')
assert status == 200 and len(demo['tables']) == 8 and len(demo['relations']) == 9
assert any(c['comment'] for c in demo['tables'][0]['columns'])
with tempfile.TemporaryDirectory() as directory:
    path = os.path.join(directory, 'schema-test.db')
    with sqlite3.connect(path) as db:
        db.executescript('''
        CREATE TABLE "parent'quoted" (a INTEGER, b INTEGER, title VARCHAR(80) NOT NULL DEFAULT 'hello', PRIMARY KEY (a,b));
        CREATE TABLE child (id INTEGER PRIMARY KEY, a INTEGER, b INTEGER, price DECIMAL(12,2), FOREIGN KEY (a,b) REFERENCES "parent'quoted" (a,b));
        CREATE TABLE implicit_ref (a INTEGER, b INTEGER, FOREIGN KEY(a,b) REFERENCES "parent'quoted");
        CREATE TABLE empty_table (x TEXT);
        ''')
    status, schema = call('/api/schema', {'provider':'sqlite','connectionString':f'Data Source={path}'})
    assert status == 200, schema
    assert len(schema['tables']) == 4 and len(schema['relations']) == 4
    tables = {t['name']:t for t in schema['tables']}
    cols = {c['name']:c for c in tables["parent'quoted"]['columns']}
    assert cols['a']['primaryKey'] and cols['b']['primaryKey']
    assert cols['title']['length'] == '80' and not cols['title']['nullable']
    assert cols['title']['defaultValue'] == "'hello'"
    price = next(c for c in tables['child']['columns'] if c['name']=='price')
    assert price['type'] == 'DECIMAL' and price['length'] == '12,2' and price['nullable']
    implicit = [r for r in schema['relations'] if r['fromTable'] == 'implicit_ref']
    assert [(r['fromColumn'],r['toColumn']) for r in implicit] == [('a','a'),('b','b')]
    missing = os.path.join(directory,'must-not-be-created.db')
    assert call('/api/schema', {'provider':'sqlite','connectionString':f'Data Source={missing}'})[0] == 400
    assert not os.path.exists(missing)
assert call('/api/schema', {'provider':'invalid','connectionString':'secret-password'})[0] == 400
assert call('/api/schema', {'provider':'sqlite','connectionString':''})[0] == 400
assert call('/api/schema', {'provider':'sqlite','connectionString':'anything'}, 'https://untrusted.example')[0] == 403
print('PASS: demo, SQLite metadata, composite keys, implicit foreign keys, quoted names, defaults, read-only mode, validation, origin guard')
