FROM node:22-alpine
ENV NODE_ENV=production HOST=0.0.0.0
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY src ./src
COPY migrations ./migrations
COPY public ./public
USER node
EXPOSE 3000
# Run the migrations with the same image: docker run ... node src/migrate.js
CMD ["node", "src/server.js"]
