# Language Sources

Edit language strings in `langsrc/<locale>/*.json`.

The build merges these files into the Vintage Story asset file at
`assets/vintagekinematics/lang/<locale>.json`.

`_order.txt` preserves the generated file's key order so splitting the source
does not cause unnecessary churn in the generated asset. Add new keys to any
category file; they will be appended after the ordered keys until `_order.txt`
is refreshed.
