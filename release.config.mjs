/**
 * semantic-release configuration.
 *
 * - `main` publishes stable versions (vX.Y.Z); `next` publishes pre-releases (vX.Y.Z-pre.N).
 * - Only a tag and a GitHub release are created; nothing is committed back to the repository.
 * - Tags pushed with GITHUB_TOKEN do not trigger other workflows, so the Docker pipeline is
 *   dispatched explicitly for the new tag; it appends the image details to the release notes.
 */
export default {
  branches: ['main', { name: 'next', prerelease: 'pre' }],
  tagFormat: 'v${version}',
  plugins: [
    ['@semantic-release/commit-analyzer', { preset: 'conventionalcommits' }],
    ['@semantic-release/release-notes-generator', { preset: 'conventionalcommits' }],
    [
      '@semantic-release/github',
      { successComment: false, failComment: false, releasedLabels: false },
    ],
    [
      '@semantic-release/exec',
      { successCmd: 'gh workflow run docker.yml --ref "${nextRelease.gitTag}"' },
    ],
  ],
};
