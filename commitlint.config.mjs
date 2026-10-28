export default {
  extends: ['@commitlint/config-conventional'],
  // Merge commits created by GitHub ("Merge pull request #N from …") are not release-relevant.
  ignores: [(message) => message.startsWith('Merge ')],
};
