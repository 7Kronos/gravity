# Gravity.Dsl.Emitter.JsonSchema

JSON Schema (Draft-07) reference emitter for the Gravity DSL. Consumed alongside
`Gravity.Dsl.MsBuild` via `<PackageReference>` in the same two-package layout the
outline sample emitter documents. The emitter produces one JSON Schema bundle
file per entity (entity-state root + per-event payload + per-command
request/response in `definitions`), plus one stand-alone file per namespace-scope
value type and per enum. Output is byte-deterministic Draft-07; configuration
takes `output` (required) and `bundle_strategy: per-entity` (the only legal
value in v1).

## Configuration

```yaml
emitters:
  json-schema:
    output: gen/json-schema
    bundle_strategy: per-entity   # optional; the only legal value in v1
```

## Notes

- `Decimal` values are emitted as `{ "type": "string", "format": "decimal" }`
  (vendor format) to preserve precision. JSON's `number` is IEEE-754 double and
  cannot losslessly represent regulatory decimal values.
- `Long` values outside the JavaScript safe-integer range (`±2^53`) lose
  precision in JSON parsers that decode `integer` as IEEE-754 double. AJV and
  JsonSchema.Net handle the full int64 range correctly.
- Every object schema carries `"additionalProperties": false`. The schema is the
  read-only contract; open-content schemas are not an option in v1.
- Output is Draft-07 (`http://json-schema.org/draft-07/schema#`). Multi-dialect
  dispatch (Draft 2020-12) is future work.
