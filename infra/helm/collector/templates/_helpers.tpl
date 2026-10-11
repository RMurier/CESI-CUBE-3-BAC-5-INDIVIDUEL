{{/*
Nom complet d'une ressource pour un service donné (ex: "collector-identity").
*/}}
{{- define "collector.fullname" -}}
{{- printf "collector-%s" .serviceName -}}
{{- end -}}

{{/*
Labels communs appliqués à toute ressource du service $svcName.
Usage : {{ include "collector.labels" (dict "root" $ "serviceName" $svcName) }}
*/}}
{{- define "collector.labels" -}}
app.kubernetes.io/name: {{ .serviceName }}
app.kubernetes.io/part-of: collector
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
collector.shop/environment: {{ .root.Values.global.environment }}
{{- end -}}

{{/*
Image complète ghcr.io/<owner>/<service>:<tag> d'un service.
*/}}
{{- define "collector.image" -}}
{{- printf "%s/%s:%s" .root.Values.global.imageRegistry .image .root.Values.global.imageTag -}}
{{- end -}}

{{/*
Nom du Secret K8s synchronisé par Infisical, commun à tous les services du namespace.
*/}}
{{- define "collector.secretName" -}}
collector-secrets
{{- end -}}

{{/*
Nom du ConfigMap partagé (URLs de service discovery K8s, host RabbitMQ, etc.).
*/}}
{{- define "collector.configMapName" -}}
collector-config
{{- end -}}
