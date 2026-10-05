# VR component and Lambda editing

The component inspector supports **Quitar objeto**, removing the selected local component and all its connections. **Deshacer** restores the graph. Removing a component does not delete a deployed AWS resource. The existing slot cleanup review remains the AWS deletion path.

Choose a supported property and press **Usar ajuste al crear** to remember it for future components of that service on this device. Manual placement and direct creation use the saved default. ATLAS receives the default alongside allowed settings. Existing components and presets retain their settings. These defaults cover Lambda memory, DynamoDB capacity, S3 versioning, SQS type and CloudWatch retention; API/EventBridge retain the compiler's single supported option. They do not introduce unsupported AWS settings.

Lambda studio includes Python token colors with escaped source markup. **Cambios** aligns inserted/deleted lines against the last loaded AWS source, includes unchanged context, and labels old/new line numbers. Very large line counts use a bounded line comparison. Long lines remain paginated rather than hidden.

**Evento ejemplo** retrieves a source event from an incoming connection to the current Lambda. S3 examples contain an explicit bucket placeholder; they require actual AWS data and permissions. **Salida esperada** accepts the complete handler return value as JSON, not just its HTTP body. Blank means execution-only testing. JSON object key order is ignored; array order and value types must match. For example, a doubling handler can use event `{"amount":21}` and expected output `{"total":42}`. The response `{"statusCode":200,"body":"..."}` must instead be asserted as that complete object.

**Guardar caso** saves up to 12 named cases per Lambda draft. Reusing a name replaces that case. **Elegir caso** cycles cases; **Borrar caso** removes the selected saved case. Cases and expected values persist locally. Source, event or expected-output changes invalidate the previous test result. Each **Probar borrador** runs the current case only, in the existing limited-role test function; saving/selecting cases never executes code. Expected-output assertions require the updated backend to be deployed.

**Exportar .py** writes UTF-8 `index.py` and `tests.json` into `Application.persistentDataPath/lambda-exports/<draft identity hash>/`. The panel displays the directory. Exporting does not publish code to AWS. On Quest, retrieve the files through device storage/developer tooling.

ATLAS context retrieves up to eight compiler-backed recipes, prioritizing links attached to the selected component. Each recipe includes current node IDs, normalized event shape, integration code hints, source provenance and limitations. Actual physical destinations come from the deployed code's `TARGETS` environment entries (`kind`, `name`, `nodeId`), never visible labels. Removing nodes removes their recipes from subsequent context. Saved Lambda cases are available through `get_lambda_code`. This is local graph-based retrieval; it does not index outside documents or contact a vector database.

Routine spoken confirmations remain capped at 15 words. Explanations default to two short sentences and 35 words unless the user requests more; code and detailed evidence stay in panels and tools. These instructions require a new session from the updated broker. No model or pricing change is included.

Validation: backend unit suite, compiler template checks, Unity editor suite, and Unity PlayMode interaction suite. Physical Quest readability/performance and live backend deployment remain separate acceptance steps.
