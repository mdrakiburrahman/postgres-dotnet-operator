#!/bin/bash

echo "Configuring wakatime..."
printf "\n[settings]\napi_key = $WAKA_TIME_API_KEY\n" > ~/.wakatime.cfg;

exec "$@"