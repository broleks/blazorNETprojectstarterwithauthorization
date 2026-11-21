#!/bin/bash
# Configures the local Elasticsearch and Kibana for the starter's logs. Run by the "elastic-setup" service in
# compose.yaml (profile "logs"); safe to run again. For Elastic Cloud or another cluster run the same two requests
# with your URL and an API key (see docs/logging.md).
set -euo pipefail
ES="${ELASTICSEARCH_URL:-http://elasticsearch:9200}"
KIBANA="${KIBANA_URL:-http://kibana:5601}"
RETENTION="${LOG_RETENTION_DAYS:-7}d"

echo "Waiting for Elasticsearch at $ES..."
until curl -fs "$ES/_cluster/health?wait_for_status=yellow&timeout=5s" > /dev/null; do sleep 2; done

# Data streams logs-starter.<app>-<namespace>: ECS mappings and automatic deletion after the retention period.
sed "s/__RETENTION__/$RETENTION/" /setup/index-template.json > /tmp/template.json
curl -sS --fail-with-body -X PUT "$ES/_index_template/logs-starter" -H 'Content-Type: application/json' --data-binary @/tmp/template.json
echo
# Existing data streams keep their own lifecycle; update them too so a changed retention applies immediately.
curl -fsS -X PUT "$ES/_data_stream/logs-starter.*/_lifecycle" -H 'Content-Type: application/json' \
  -d "{\"data_retention\": \"$RETENTION\"}" > /dev/null || true

echo "Waiting for Kibana at $KIBANA..."
until curl -fs "$KIBANA/api/status" | grep -q '"level":"available"'; do sleep 3; done

# One data view over all three applications; filter by service.name, user.id, trace.id or event.action.
curl -sS --fail-with-body -X POST "$KIBANA/api/data_views/data_view" -H 'kbn-xsrf: true' -H 'Content-Type: application/json' -d '{
  "override": true,
  "data_view": {
    "id": "starter-logs",
    "name": "Starter logs",
    "title": "logs-starter.*",
    "timeFieldName": "@timestamp"
  }
}' > /dev/null
echo "Kibana is ready: http://localhost:5601 (Discover -> data view 'Starter logs'), retention $RETENTION."
