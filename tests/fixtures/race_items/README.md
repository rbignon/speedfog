# Race items catalog corpus

Catalogs replayed by both loaders of `data/plugins/race_items.toml`, so the two
stay in step: the Python one (`speedfog/race_items.py`, `tests/test_race_items.py`)
and the C# one (`RaceItemsCatalog`, `RaceItemsCatalogCorpusTests`).

- `valid_*.toml`: both loaders accept it.
- `invalid_*.toml`: both loaders reject it, each with a message containing the
  text after `# expect: ` on the file's first line.

A rule added to one loader gets a file here, which fails the other loader's
tests until it has the rule too. `[[junk_weapon]]` entries are read by Python
only (they reach the racing mod through graph.json): the C# loader checks the
key and that it is an array of tables, so only that rule has a file here.

A TOML syntax error (a duplicate key, an unclosed string) is rejected by both
parsers before either loader runs, with the parser's own exception type, so
it has no file here either.
