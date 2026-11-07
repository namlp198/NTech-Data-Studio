# Image chay worker cua NTech Trainer. Build tu goc repo:
#   docker build -f docker/trainer/Dockerfile --build-arg COMPUTE=cpu -t ntech-trainer:dev-cpu .
# COMPUTE=cpu hoac cuda, chon file khoa phien ban tuong ung.
FROM python:3.12-slim-bookworm

ARG COMPUTE=cpu
ARG TORCH_INDEX=https://download.pytorch.org/whl/cpu

# OpenCV can hai thu vien he thong nay.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

COPY docker/trainer/lock-${COMPUTE}.txt /opt/ntech/lock.txt
RUN pip install --no-cache-dir --index-url "${TORCH_INDEX}" --extra-index-url https://pypi.org/simple -r /opt/ntech/lock.txt

COPY trainer/python/ntech_worker /opt/ntech/ntech_worker

ENV PYTHONPATH=/opt/ntech \
    PYTHONUNBUFFERED=1 \
    YOLO_CONFIG_DIR=/cache/ultralytics-config \
    MPLCONFIGDIR=/tmp/matplotlib

ENTRYPOINT ["python", "-m", "ntech_worker"]
