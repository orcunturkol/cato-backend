# Phase 2 gate probe. Runs inside the VPC as a one-off Fargate task; prints names, codes
# and open/closed ports only. Args: <label> <mq host> <cache host> <rds host> <send test alert: yes|no>
label=$1 mq=$2 cache=$3 rds=$4 alert=$5
echo "== probe $label"
for h in api frontend worker ogt-api seq; do echo "dns $h.cato.internal -> $(getent hosts $h.cato.internal | awk '{print $1}' | tr '\n' ' ')"; done
tcp() { if timeout 5 bash -c "</dev/tcp/$1/$2" 2>/dev/null; then echo "tcp $3 open"; else echo "tcp $3 closed"; fi; }
tcp api.cato.internal 8080 api:8080
tcp frontend.cato.internal 80 frontend:80
tcp "$mq" 5671 mq:5671
tcp "$cache" 6379 cache:6379
tcp "$rds" 5432 rds:5432
if timeout 5 bash -c "</dev/tcp/api.cato.internal/8080" 2>/dev/null; then
  echo "api /health/ready: $(curl -s -m 15 http://api.cato.internal:8080/health/ready | tr -d '\n ' | cut -c1-300)"
  echo "api games: $(curl -s -o /dev/null -w '%{http_code}' -m 30 'http://api.cato.internal:8080/api/games?page=1')"
  [ "$alert" = yes ] && echo "test alert: $(curl -s -m 30 -X POST -H 'Content-Type: application/json' -d '{}' http://api.cato.internal:8080/api/alerts/test)"
fi
if timeout 5 bash -c "</dev/tcp/frontend.cato.internal/80" 2>/dev/null; then
  echo "frontend /: $(curl -s -o /dev/null -w '%{http_code}' -m 15 http://frontend.cato.internal/)"
fi
echo "== end $label"
